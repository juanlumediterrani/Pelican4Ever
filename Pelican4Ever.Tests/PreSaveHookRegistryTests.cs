using System;
using System.Collections.Generic;
using Pelican4Ever.Core.Admin;
using Xunit;

namespace Pelican4Ever.Tests
{
    public sealed class PreSaveHookRegistryTests
    {
        [Fact]
        public void Register_InvokeAll_AndUnregister_ExecuteCleanlyWithoutDuplicates()
        {
            var registry = new PreSaveHookRegistry();
            int calls = 0;
            Action hook = () => calls++;

            registry.Register(hook);
            registry.Register(hook);
            Assert.Equal(1, registry.Count);

            int succeeded = registry.InvokeAll();
            Assert.Equal(1, succeeded);
            Assert.Equal(1, calls);

            registry.Unregister(hook);
            Assert.Equal(0, registry.Count);
            Assert.Equal(0, registry.InvokeAll());
            Assert.Equal(1, calls);
        }

        [Fact]
        public void InvokeAll_IsolatesExceptionsAndContinuesRemainingCallbacks()
        {
            var registry = new PreSaveHookRegistry();
            var errors = new List<Exception>();
            bool secondRan = false;

            registry.Register(() => throw new InvalidOperationException("fallo simulado"));
            registry.Register(() => secondRan = true);

            int succeeded = registry.InvokeAll(errors.Add);

            Assert.Equal(1, succeeded);
            Assert.True(secondRan);
            Assert.Single(errors);
        }
    }
}
