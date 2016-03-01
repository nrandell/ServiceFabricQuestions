using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TimingService31
{

    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance.
    /// </summary>
    internal sealed class TimingService31 : StatefulService
    {

        protected override IReliableStateManager CreateReliableStateManager()
        {
            return new ReliableStateManager(
                new ReliableStateManagerConfiguration(
                    new ReliableStateManagerReplicatorSettings
                    {
                        BatchAcknowledgementInterval = TimeSpan.FromMilliseconds(0.05)
                    }
                )
            );
        }

        protected override async Task RunAsync(CancellationToken cancelServicePartitionReplica)
        {
            var readers = 4;
            var writers = 4;
            var queueCount = 1;

            var cancelled = new SemaphoreSlim(0);
            cancelServicePartitionReplica.Register(() =>
            {
                ServiceEventSource.Current.ServiceMessage(this, "Service cancellation requested");
                cancelled.Release();
            });
            try
            {
                counter = 0;

                var queues = new IReliableQueue<long>[queueCount];
                for (var i = 0; i < queueCount; i++)
                {
                    queues[i] = await StateManager.GetOrAddAsync<IReliableQueue<long>>($"Queue-{i}");
                }
                var tasks = new List<Task>();
                tasks.Add(ProgressRunner(cancelServicePartitionReplica));

                for (var writer = 0; writer < writers; writer++)
                {
                    var queue = queues[writer % queueCount];
                    var index = writer;
                    tasks.Add(WriterRunner(queue, index, cancelServicePartitionReplica));
                }

                for (var reader = 0; reader < readers; reader++)
                {
                    var queue = queues[reader % queueCount];
                    var index = reader;
                    tasks.Add(ReaderRunner(queue, index, cancelServicePartitionReplica));
                }

                ServiceEventSource.Current.ServiceMessage(this, "All running, now waiting ...");
                await cancelled.WaitAsync();
                ServiceEventSource.Current.ServiceMessage(this, "Cancelled, now waiting for tasks to finish");
                var allTasks = Task.WhenAll(tasks);
                var slowDelayTask = Task.Delay(TimeSpan.FromSeconds(10));
                await Task.WhenAny(allTasks, slowDelayTask);
                var totalCompleted = tasks.Count(t => t.IsCompleted);
                var totalFaulted = tasks.Count(t => t.IsFaulted);
                var totalCancelled = tasks.Count(t => t.IsCanceled);
                ServiceEventSource.Current.ServiceMessage(this, "Total = {0}, Completed = {1}, Faulted = {2}, Cancelled = {3}", tasks.Count, totalCompleted, totalFaulted, totalCancelled);
                if (slowDelayTask.IsCompleted)
                {
                    ServiceEventSource.Current.ServiceMessage(this, "Tasks have not finished, exiting");
                    System.Environment.Exit(-1);
                }
                else
                {
                    ServiceEventSource.Current.ServiceMessage(this, "All tasks have finished");
                }
            }
            catch (OperationCanceledException)
            {
                ServiceEventSource.Current.ServiceMessage(this, "RunAsync cancelled");
                // ignore
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.ServiceMessage(this, "Error {0}", ex.Message);
            }
            finally
            {
                ServiceEventSource.Current.ServiceMessage(this, "RunAsync finished");
            }
        }


        async Task ProgressRunner(CancellationToken cancelServicePartitionReplica)
        {

            try {
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
            catch (OperationCanceledException)
            {
                return;
            }
            catch (FabricException)
            {
                return;
            }
            finally
            {
                ServiceEventSource.Current.ServiceMessage(this, "Progress finished");
            }
        }

        long counter;


        async Task WriterRunner(IReliableQueue<long> queue, int instance, CancellationToken cancelServicePartitionReplica)
        {
            try {
                var nextInput = 0L;
                while (!cancelServicePartitionReplica.IsCancellationRequested)
                {
                    try
                    {
                        using (var tx = this.StateManager.CreateTransaction())
                        {
                            await queue.EnqueueAsync(tx, nextInput);
                            cancelServicePartitionReplica.ThrowIfCancellationRequested();
                            await tx.CommitAsync();
                            cancelServicePartitionReplica.ThrowIfCancellationRequested();
                        }
                        nextInput += 1;
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (FabricException)
                    {
                        return;
                    }
                    catch (TimeoutException)
                    {
                        ServiceEventSource.Current.ServiceMessage(this, "Writer {0} Timeout", instance);
                    }
                }
            }
            finally
            {
                ServiceEventSource.Current.ServiceMessage(this, "Writer {0} finishing", instance);
            }
        }

        async Task ReaderRunner(IReliableQueue<long> queue, int instance, CancellationToken cancelServicePartitionReplica)
        {
            try
            {
                while (!cancelServicePartitionReplica.IsCancellationRequested)
                {
                    try
                    {
                        ConditionalResult<long> result;
                        using (var tx = this.StateManager.CreateTransaction())
                        {
                            result = await queue.TryDequeueAsync(tx);
                            cancelServicePartitionReplica.ThrowIfCancellationRequested();
                            await tx.CommitAsync();
                            cancelServicePartitionReplica.ThrowIfCancellationRequested();
                        }
                        if (result.HasValue)
                        {
                            Interlocked.Increment(ref counter);
                        }
                        else
                        {
                            await Task.Delay(1, cancelServicePartitionReplica);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (FabricException)
                    {
                        return;
                    }
                    catch (TimeoutException)
                    {
                        ServiceEventSource.Current.ServiceMessage(this, "Reader {0} Timeout", instance);
                    }
                }
            }
            finally
            {
                ServiceEventSource.Current.ServiceMessage(this, "Reader {0} finishing", instance);
            }
        }
    }
}
