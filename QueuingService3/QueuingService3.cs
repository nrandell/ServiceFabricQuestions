using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QueuingService3
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance.
    /// </summary>
    internal sealed class QueuingService3 : StatefulService
    {
        protected override async Task RunAsync(CancellationToken cancelServicePartitionReplica)
        {
            var myQueue = await StateManager.GetOrAddAsync<IReliableQueue<long>>("myQueue");
            var enqueuing = EnqueuingTask(myQueue, cancelServicePartitionReplica);
            var dequeuing = DequeuingTask(myQueue, cancelServicePartitionReplica);
            await Task.WhenAll(enqueuing, dequeuing);
        }

        async Task DequeuingTask(IReliableQueue<long> myQueue, CancellationToken cancelServicePartitionReplica)
        {
            while (!cancelServicePartitionReplica.IsCancellationRequested)
            {
                ConditionalResult<long> result;
                using (var tx = StateManager.CreateTransaction())
                {
                    result = await myQueue.TryDequeueAsync(tx);
                    await tx.CommitAsync();
                }
                if (result.HasValue)
                {
                    ServiceEventSource.Current.ServiceMessage(this, "Dequeued {0} {1} {2}",
                        ServiceInitializationParameters.ServiceName,
                        ServiceInitializationParameters.PartitionId,
                        result.Value);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancelServicePartitionReplica);
                }
            }
        }

        async Task EnqueuingTask(IReliableQueue<long> myQueue, CancellationToken cancelServicePartitionReplica)
        {
            var value = 0L;
            while (!cancelServicePartitionReplica.IsCancellationRequested)
            {
                using (var tx = StateManager.CreateTransaction())
                {
                    await myQueue.EnqueueAsync(tx, value);
                    await tx.CommitAsync();
                }
                value += 1;
                await Task.Delay(TimeSpan.FromMilliseconds(900), cancelServicePartitionReplica);
            }
        }
    }
}
