﻿using System;
using System.Linq;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Router;

class ForwardUnsubscribeMessageDrivenRule : IRule<ForwardUnsubscribeContext, ForwardUnsubscribeContext>
{
    string localAddress;
    string localEndpoint;

    public ForwardUnsubscribeMessageDrivenRule(string localAddress, string localEndpoint)
    {
        this.localAddress = localAddress;
        this.localEndpoint = localEndpoint;
    }

    public async Task Invoke(ForwardUnsubscribeContext context, Func<ForwardUnsubscribeContext, Task> next)
    {
        var immediateSubscribes = context.Routes.Where(r => r.Gateway == null);
        var forkContexts = immediateSubscribes.Select(r =>
            new MulticastContext(r.Destination,
                MessageDrivenPubSub.CreateMessage(null, context.MessageType, localAddress, localEndpoint, MessageIntentEnum.Unsubscribe), context));

        var chain = context.Chains.Get<MulticastContext>();
        var forkTasks = forkContexts.Select(c => chain.Invoke(c));
        await Task.WhenAll(forkTasks).ConfigureAwait(false);
        await next(context);
    }
}