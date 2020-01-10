﻿namespace MassTransit.EntityFrameworkCoreIntegration.Saga
{
    using System;
    using System.Data;
    using System.Linq;
    using MassTransit.Saga;
    using Microsoft.EntityFrameworkCore;


    public static class EntityFrameworkSagaRepository<TSaga>
        where TSaga : class, ISaga
    {
        public static ISagaRepository<TSaga> CreateOptimistic(ISagaDbContextFactory<TSaga> dbContextFactory,
            Func<IQueryable<TSaga>, IQueryable<TSaga>> queryCustomization = null)
        {
            ILoadQueryProvider<TSaga> queryProvider = new DefaultSagaLoadQueryProvider<TSaga>();
            if (queryCustomization != null)
                queryProvider = new CustomSagaLoadQueryProvider<TSaga>(queryProvider, queryCustomization);

            var queryExecutor = new OptimisticLoadQueryExecutor<TSaga>(queryProvider);
            var lockStrategy = new OptimisticSagaRepositoryLockStrategy<TSaga>(queryProvider, queryExecutor);

            return CreateRepository(dbContextFactory, lockStrategy, IsolationLevel.ReadCommitted);
        }

        public static ISagaRepository<TSaga> CreateOptimistic(Func<DbContext> dbContextFactory,
            Func<IQueryable<TSaga>, IQueryable<TSaga>> queryCustomization = null)
        {
            return CreateOptimistic(new DelegateSagaDbContextFactory<TSaga>(dbContextFactory), queryCustomization);
        }

        public static ISagaRepository<TSaga> CreatePessimistic(ISagaDbContextFactory<TSaga> dbContextFactory,
            ILockStatementProvider lockStatementProvider = null,
            Func<IQueryable<TSaga>, IQueryable<TSaga>> queryCustomization = null)
        {
            var statementProvider = lockStatementProvider ?? new SqlServerLockStatementProvider();

            ILoadQueryProvider<TSaga> queryProvider = new DefaultSagaLoadQueryProvider<TSaga>();
            if (queryCustomization != null)
                queryProvider = new CustomSagaLoadQueryProvider<TSaga>(queryProvider, queryCustomization);

            var queryExecutor = new PessimisticLoadQueryExecutor<TSaga>(queryProvider, statementProvider);
            var lockStrategy = new PessimisticSagaRepositoryLockStrategy<TSaga>(queryExecutor);

            return CreateRepository(dbContextFactory, lockStrategy, IsolationLevel.Serializable);
        }

        public static ISagaRepository<TSaga> CreatePessimistic(Func<DbContext> dbContextFactory, ILockStatementProvider lockStatementProvider = null,
            Func<IQueryable<TSaga>, IQueryable<TSaga>> queryCustomization = null)
        {
            return CreatePessimistic(new DelegateSagaDbContextFactory<TSaga>(dbContextFactory), lockStatementProvider, queryCustomization);
        }

        static ISagaRepository<TSaga> CreateRepository(ISagaDbContextFactory<TSaga> dbContextFactory, ISagaRepositoryLockStrategy<TSaga> lockStrategy,
            IsolationLevel isolationLevel)
        {
            var consumeContextFactory = new EntityFrameworkSagaConsumeContextFactory<TSaga>();

            var repositoryFactory =
                new EntityFrameworkSagaRepositoryContextFactory<TSaga>(dbContextFactory, consumeContextFactory, isolationLevel, lockStrategy);

            return new SagaRepository<TSaga>(repositoryFactory);
        }
    }
}
