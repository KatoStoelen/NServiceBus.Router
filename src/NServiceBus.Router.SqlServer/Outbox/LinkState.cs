﻿namespace NServiceBus.Router.Deduplication.Outbox
{
    using System;
    using System.Collections.Generic;
    using System.Data.SqlClient;
    using Transport;

    class LinkState
    {
        public long Epoch { get; }
        /// <summary>
        /// Has the recent epoch change been announced.
        /// </summary>
        public bool IsEpochAnnounced { get; }
        public SessionState HeadSession { get; }
        public SessionState TailSession { get; }

        public static LinkState Hydrate(SqlDataReader reader)
        {
            var epoch = reader.GetInt64(0);
            var announced = reader.GetBoolean(1);
            var headLo = reader.GetInt64(2);
            var headHi = reader.GetInt64(3);
            var headTable = reader.IsDBNull(4) ? null : reader.GetString(4);
            var tailLo = reader.GetInt64(5);
            var tailHi = reader.GetInt64(6);
            var tailTable = reader.IsDBNull(7) ? null : reader.GetString(7);

            var headSession = headTable == null ? null : new SessionState(headLo, headHi, headTable);
            var tailSession = tailTable == null ? null : new SessionState(tailLo, tailHi, tailTable);
            return new LinkState(epoch, announced, headSession, tailSession);
        }

        LinkState(long epoch, bool announced, SessionState headSession, SessionState tailSession)
        {
            Epoch = epoch;
            IsEpochAnnounced = announced;
            HeadSession = headSession;
            TailSession = tailSession;
        }

        public LinkState Advance(int epochSize)
        {
            return new LinkState(Epoch + 1, false,
                new SessionState(HeadSession.Hi, HeadSession.Hi + epochSize, TailSession.Table), 
                HeadSession);
        }

        public (LinkState, OutgoingMessage) AnnounceAdvance(string sourceKey)
        {
            var newState = new LinkState(Epoch, true, HeadSession, TailSession);

            var headers = new Dictionary<string, string>
            {
                [RouterDeduplicationHeaders.SequenceKey] = sourceKey,
                [RouterDeduplicationHeaders.Advance] = "true",
                [RouterDeduplicationHeaders.AdvanceEpoch] = Epoch.ToString(),
                [RouterDeduplicationHeaders.AdvanceHeadLo] = HeadSession.Lo.ToString(),
                [RouterDeduplicationHeaders.AdvanceHeadHi] = HeadSession.Hi.ToString(),
            };

            var message = new OutgoingMessage(Guid.NewGuid().ToString(), headers, new byte[0]);
            return (newState, message);
        }

        public (LinkState, OutgoingMessage) AnnounceInitialize(string sourceKey)
        {
            var newState = new LinkState(Epoch, true, HeadSession, TailSession);

            var headers = new Dictionary<string, string>
            {
                [RouterDeduplicationHeaders.SequenceKey] = sourceKey,
                [RouterDeduplicationHeaders.Initialize] = "true",
                [RouterDeduplicationHeaders.InitializeHeadLo] = HeadSession.Lo.ToString(),
                [RouterDeduplicationHeaders.InitializeHeadHi] = HeadSession.Hi.ToString(),
                [RouterDeduplicationHeaders.InitializeTailLo] = TailSession.Lo.ToString(),
                [RouterDeduplicationHeaders.InitializeTailHi] = TailSession.Hi.ToString()
            };

            var message = new OutgoingMessage(Guid.NewGuid().ToString(), headers, new byte[0]);
            return (newState, message);
        }

        public bool Initialized => HeadSession != null;

        public bool IsStale(long seq)
        {
            return seq >= HeadSession.Hi;
        }

        public bool ShouldAdvance(long seq)
        {
            var threshold = HeadSession.Lo + HeadSession.EpochSize / 2;
            return seq >= threshold;
        }

        public string GetTableName(long sequence)
        {
            if (HeadSession.Matches(sequence))
            {
                return HeadSession.Table;
            }
            if (TailSession.Matches(sequence))
            {
                return TailSession.Table;
            }
            throw new Exception($"Provided sequence {sequence} does not match the current epoch [{HeadSession.Lo},{HeadSession.Hi}),[{TailSession.Lo},{TailSession.Hi})");
        }

        public static LinkState Uninitialized()
        {
            return new LinkState(0, false, null, null);
        }

        public LinkState Initialize(string headTable, string tailTable, int epochSize)
        {
            if (Initialized)
            {
                throw new NotSupportedException("The link sate object has already been initialized.");
            }
            var newState = new LinkState(1, false,
                new SessionState(epochSize, epochSize + epochSize, headTable),
                new SessionState(0, epochSize, tailTable));

            return newState;
        }

        public override string ToString()
        {
            return $"{Epoch},[{HeadSession.Lo},{HeadSession.Hi}),[{TailSession.Lo},{TailSession.Hi}]";
        }
    }
}