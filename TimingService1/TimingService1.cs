using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TimingService1
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance.
    /// </summary>
    internal sealed class TimingService1 : StatefulService
    {

        protected override IReliableStateManager CreateReliableStateManager()
        {
            //return base.CreateReliableStateManager();
            return new ReliableStateManager(
                new ReliableStateManagerConfiguration(
                    new ReliableStateManagerReplicatorSettings
                    {
                        BatchAcknowledgementInterval = TimeSpan.FromMilliseconds(100)
                    }
                )
            );

            //var value = 16384;
            //return new ReliableStateManager(
            //    new ReliableStateManagerConfiguration(
            //        new ReliableStateManagerReplicatorSettings
            //        {
            //            BatchAcknowledgementInterval = TimeSpan.FromMilliseconds(50),
            //            CheckpointThresholdInMB = 50,
            //            InitialCopyQueueSize = value,
            //            InitialPrimaryReplicationQueueSize = value,
            //            InitialSecondaryReplicationQueueSize = value,
            //            MaxCopyQueueSize = value,
            //            MaxMetadataSizeInKB = 4,
            //            MaxPrimaryReplicationQueueMemorySize = 0,
            //            MaxPrimaryReplicationQueueSize = value,
            //            MaxRecordSizeInKB = 1024,
            //            MaxReplicationMessageSize = 50 * 1024 * 1024,
            //            MaxSecondaryReplicationQueueMemorySize = 0,
            //            MaxSecondaryReplicationQueueSize = value,
            //            MaxWriteQueueDepthInKB = 0,
            //            OptimizeLogForLowerDiskUsage = false,
            //            ReplicatorAddress = "localhost:0",
            //            RetryInterval = TimeSpan.FromSeconds(5),
            //            SecondaryClearAcknowledgedOperations = false,
            //            SharedLogId = "",
            //            SharedLogPath = ""
            //        }
            //    )
            //);
        }

        protected override async Task RunAsync(CancellationToken cancelServicePartitionReplica)
        {
            try {
                var numberOfTasks = 1000;
                var tasks = new List<Task>();
                counter = 0;
                tasks.Add(ProgressRunner(cancelServicePartitionReplica));

                var myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, long>>("myDictionary");
                for (var i = 0; i < numberOfTasks; i++)
                {
                    var key = $"Counter-{i}";
                    var task = InstanceRunner(myDictionary, key, cancelServicePartitionReplica);
                    tasks.Add(task);
                }

                await Task.WhenAny(tasks);
                ServiceEventSource.Current.ServiceMessage(this, "Finished");
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.ServiceMessage(this, "Finished due to {0}", ex.Message);
                throw ex;
            }
        }

        async Task ProgressRunner(CancellationToken cancelServicePartitionReplica)
        {
            var sw = new Stopwatch();
            sw.Start();

            var totalCounted = 0L;
            var totalElapsed = 0.0;

            // This partition's replica continues processing until the replica is terminated.
            while (!cancelServicePartitionReplica.IsCancellationRequested)
            {
                await Task.Delay(1000, cancelServicePartitionReplica);
                var elapsed = sw.Elapsed;
                sw.Restart();
                var counted = Interlocked.Exchange(ref counter, 0);
                var average = counted / elapsed.TotalSeconds;
                totalCounted += counted;
                totalElapsed += elapsed.TotalSeconds;
                var overall = totalCounted / totalElapsed;

                ServiceEventSource.Current.ServiceMessage(this, "{2}:{3}:Counted {0} in {1} avg {4}/s, overall {5}/s", counted, elapsed.TotalSeconds,
                    ServiceInitializationParameters.ServiceName, ServiceInitializationParameters.PartitionId, average, overall);

            }

        }

        long counter;

        async Task InstanceRunner(IReliableDictionary<string, long> dictionary, string key, CancellationToken cancelServicePartitionReplica)
        {
            while (!cancelServicePartitionReplica.IsCancellationRequested)
            {
                try {
                    // Create a transaction to perform operations on data within this partition's replica.
                    using (var tx = this.StateManager.CreateTransaction())
                    {
                        await dictionary.TryGetValueAsync(tx, key);
                        await dictionary.AddOrUpdateAsync(tx, key, 0, (k, v) => ++v);
                        await tx.CommitAsync();
                    }
                    Interlocked.Increment(ref counter);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (TimeoutException)
                {
                    ServiceEventSource.Current.ServiceMessage(this, "Timeout");
                }
            }

        }
    }
}
