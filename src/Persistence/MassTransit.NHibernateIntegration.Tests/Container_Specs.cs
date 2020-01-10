namespace MassTransit.NHibernateIntegration.Tests
{
    namespace ContainerTests
    {
        using System;
        using System.IO;
        using System.Threading.Tasks;
        using Automatonymous;
        using GreenPipes;
        using Microsoft.Extensions.DependencyInjection;
        using NUnit.Framework;
        using TestFramework;
        using TestFramework.Sagas;


        public class Using_optimistic_concurrency :
            InMemoryTestFixture
        {
            [Test]
            public async Task Should_work_as_expected()
            {
                Task<ConsumeContext<TestStarted>> started = ConnectPublishHandler<TestStarted>();
                Task<ConsumeContext<TestUpdated>> updated = ConnectPublishHandler<TestUpdated>();

                var correlationId = NewId.NextGuid();

                await InputQueueSendEndpoint.Send(new StartTest
                {
                    CorrelationId = correlationId,
                    TestKey = "Unique"
                });

                await started;

                await InputQueueSendEndpoint.Send(new UpdateTest
                {
                    TestId = correlationId,
                    TestKey = "Unique"
                });

                await updated;
            }

            readonly IServiceProvider _provider;

            public Using_optimistic_concurrency()
            {
                _provider = new ServiceCollection()
                    .AddMassTransit(ConfigureRegistration)
                    .AddScoped<PublishTestStartedActivity>()
                    .AddSingleton(provider => new SQLiteSessionFactoryProvider(typeof(TestInstanceMap)).GetSessionFactory())
                    .BuildServiceProvider();
            }

            protected void ConfigureRegistration<T>(IRegistrationConfigurator<T> configurator)
            {
                configurator.AddSagaStateMachine<TestStateMachineSaga, TestInstance>()
                    .NHibernateRepository();

                configurator.AddBus(provider => BusControl);
            }

            protected override void ConfigureInMemoryReceiveEndpoint(IInMemoryReceiveEndpointConfigurator configurator)
            {
                configurator.UseInMemoryOutbox();
                configurator.ConfigureSaga<TestInstance>(_provider);
            }
        }


        public class Using_pessimistic_concurrency :
            InMemoryTestFixture
        {
            [Test]
            public async Task Should_work_as_expected()
            {
                Task<ConsumeContext<TestStarted>> started = ConnectPublishHandler<TestStarted>();
                Task<ConsumeContext<TestUpdated>> updated = ConnectPublishHandler<TestUpdated>();

                var correlationId = NewId.NextGuid();

                await InputQueueSendEndpoint.Send(new StartTest
                {
                    CorrelationId = correlationId,
                    TestKey = "Unique"
                });

                await started;

                await InputQueueSendEndpoint.Send(new UpdateTest
                {
                    TestId = correlationId,
                    TestKey = "Unique"
                });

                await updated;
            }

            readonly IServiceProvider _provider;

            public Using_pessimistic_concurrency()
            {
                var path = Path.Combine(AppContext.BaseDirectory, "sagas.db");

                var connectionString = $"Data Source={path};Version=3;";

                _provider = new ServiceCollection()
                    .AddMassTransit(ConfigureRegistration)
                    .AddScoped<PublishTestStartedActivity>()
                    .AddSingleton(provider => new SQLiteSessionFactoryProvider(connectionString, typeof(TestInstanceMap)).GetSessionFactory())
                    .BuildServiceProvider();
            }

            protected void ConfigureRegistration<T>(IRegistrationConfigurator<T> configurator)
            {
                configurator.AddSagaStateMachine<TestStateMachineSaga, TestInstance>()
                    .NHibernateRepository();

                configurator.AddBus(provider => BusControl);
            }

            protected override void ConfigureInMemoryReceiveEndpoint(IInMemoryReceiveEndpointConfigurator configurator)
            {
                configurator.ConfigureSaga<TestInstance>(_provider);
            }
        }


        public class TestInstance :
            SagaStateMachineInstance
        {
            public Guid CorrelationId { get; set; }

            public string CurrentState { get; set; }
            public string Key { get; set; }
        }


        class TestInstanceMap :
            SagaClassMapping<TestInstance>
        {
            public TestInstanceMap()
            {
                Lazy(false);
                Table("TestInstance");

                Property(x => x.CurrentState);
                Property(x => x.Key, x => x.UniqueKey("IX_Key"));
            }
        }


        public class TestStateMachineSaga :
            MassTransitStateMachine<TestInstance>
        {
            public TestStateMachineSaga()
            {
                InstanceState(x => x.CurrentState);

                Event(() => Updated, x => x.CorrelateById(m => m.Message.TestId));

                Initially(
                    When(Started)
                        .Then(context => context.Instance.Key = context.Data.TestKey)
                        .Activity(x => x.OfInstanceType<PublishTestStartedActivity>())
                        .TransitionTo(Active));

                During(Active,
                    When(Updated)
                        .Publish(context => new TestUpdated
                        {
                            CorrelationId = context.Instance.CorrelationId,
                            TestKey = context.Instance.Key
                        })
                        .TransitionTo(Done)
                        .Finalize());

                SetCompletedWhenFinalized();
            }

            public State Active { get; private set; }
            public State Done { get; private set; }

            public Event<StartTest> Started { get; private set; }
            public Event<UpdateTest> Updated { get; private set; }
        }


        public class UpdateTest
        {
            public Guid TestId { get; set; }
            public string TestKey { get; set; }
        }


        public class PublishTestStartedActivity :
            Activity<TestInstance>
        {
            readonly ConsumeContext _context;

            public PublishTestStartedActivity(ConsumeContext context)
            {
                _context = context;
            }

            public void Probe(ProbeContext context)
            {
                context.CreateScope("publisher");
            }

            public void Accept(StateMachineVisitor visitor)
            {
                visitor.Visit(this);
            }

            public async Task Execute(BehaviorContext<TestInstance> context, Behavior<TestInstance> next)
            {
                await _context.Publish(new TestStarted
                {
                    CorrelationId = context.Instance.CorrelationId,
                    TestKey = context.Instance.Key
                }).ConfigureAwait(false);

                await next.Execute(context).ConfigureAwait(false);
            }

            public async Task Execute<T>(BehaviorContext<TestInstance, T> context, Behavior<TestInstance, T> next)
            {
                await _context.Publish(new TestStarted
                {
                    CorrelationId = context.Instance.CorrelationId,
                    TestKey = context.Instance.Key
                }).ConfigureAwait(false);

                await next.Execute(context).ConfigureAwait(false);
            }

            public Task Faulted<TException>(BehaviorExceptionContext<TestInstance, TException> context, Behavior<TestInstance> next)
                where TException : Exception
            {
                return next.Faulted(context);
            }

            public Task Faulted<T, TException>(BehaviorExceptionContext<TestInstance, T, TException> context, Behavior<TestInstance, T> next)
                where TException : Exception
            {
                return next.Faulted(context);
            }
        }
    }
}
