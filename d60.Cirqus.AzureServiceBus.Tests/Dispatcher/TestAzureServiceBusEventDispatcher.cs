﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using d60.Cirqus.AzureServiceBus.Dispatcher;
using d60.Cirqus.Events;
using d60.Cirqus.Extensions;
using d60.Cirqus.Testing.Internals;
using d60.Cirqus.Views;
using NUnit.Framework;

namespace d60.Cirqus.AzureServiceBus.Tests.Dispatcher
{
    [TestFixture, Category(TestCategories.Azure)]
    public class TestAzureServiceBusEventDispatcher : FixtureBase, IEventDispatcher
    {
        AzureServiceBusEventDispatcherSender _sender;
        AzureServiceBusEventDispatcherReceiver _receiver;
        List<string> _stuffThatHappened;
        AutoResetEvent _resetEvent;
        InMemoryEventStore _eventStore;

        protected override void DoSetUp()
        {
            TestAzureHelper.CleanUp();

            _stuffThatHappened = new List<string>();
            _resetEvent = new AutoResetEvent(false);

            _eventStore = new InMemoryEventStore();

            _sender = new AzureServiceBusEventDispatcherSender(TestAzureHelper.ConnectionString, TestAzureHelper.TopicName);
            _receiver = new AzureServiceBusEventDispatcherReceiver(TestAzureHelper.ConnectionString, this, _eventStore, TestAzureHelper.TopicName, TestAzureHelper.SubscriptionName);
        }

        protected override void DoTearDown()
        {
            _receiver.Dispose();
        }

        [Test]
        public void ReceiverCanBeInitialized()
        {
            _receiver.Initialize(purgeExistingViews: true);

            Assert.That(_stuffThatHappened.Count, Is.EqualTo(1));
            Assert.That(_stuffThatHappened[0], Is.EqualTo(string.Format("Initialized with {0} (purge: True)", typeof(InMemoryEventStore))));
        }

        [Test]
        public void CanDispatchLikeItShould()
        {
            _receiver.Initialize();

            _sender.Dispatch(_eventStore, new[] { AnEvent(0) });

            WaitResetEvent();

            Assert.That(_stuffThatHappened.Count, Is.EqualTo(2));
            Assert.That(_stuffThatHappened[1], Is.EqualTo(string.Format("Dispatched with {0} - events: 0", typeof(InMemoryEventStore))));
        }

        DomainEvent AnEvent(int globalSeqNo)
        {
            return new SomeDomainEvent
            {
                Meta =
                {
                    {DomainEvent.MetadataKeys.GlobalSequenceNumber, globalSeqNo}
                }
            };
        }

        class SomeDomainEvent : DomainEvent
        {

        }

        void WaitResetEvent(int seconds = 5)
        {
            Console.Write("Waiting {0} seconds... ", seconds);

            if (!_resetEvent.WaitOne(TimeSpan.FromSeconds(seconds)))
            {
                Console.WriteLine("timeout!");
                throw new ApplicationException("Did not trigger reset event within 5 second timeout!");
            }

            Console.WriteLine("done!");
        }

        public void Initialize(IEventStore eventStore, bool purgeExistingViews = false)
        {
            _stuffThatHappened.Add(string.Format("Initialized with {0} (purge: {1})", eventStore, purgeExistingViews));
        }

        public void Dispatch(IEventStore eventStore, IEnumerable<DomainEvent> events)
        {
            _stuffThatHappened.Add(string.Format("Dispatched with {0} - events: {1}", eventStore, string.Join(", ", events.Select(e => e.GetGlobalSequenceNumber()))));

            _resetEvent.Set();
        }
    }
}