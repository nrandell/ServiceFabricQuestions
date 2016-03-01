using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TimingActor1.Interfaces;

namespace TimingStateless1
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    internal sealed class TimingStateless1 : StatelessService
    {
        /// <summary>
        /// This is the main entry point for your service instance.
        /// </summary>
        /// <param name="cancelServiceInstance">Canceled when Service Fabric terminates this instance.</param>
        protected override async Task RunAsync(CancellationToken cancelServiceInstance)
        {
            var numberOfTasks = 100;
            var readOnly = false;
            try
            {
                var tasks = new List<Task>();
                counter = 0;
                tasks.Add(ProgressRunner(cancelServiceInstance));

                for (var i = 0; i < numberOfTasks; i++)
                {
                    var actorId = new ActorId($"instance-{i}");
                    var actor = ActorProxy.Create<ITimingActor1>(actorId);
                    var task = InstanceRunner(actor, readOnly, cancelServiceInstance);
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

        async Task InstanceRunner(ITimingActor1 actor, bool readOnly, CancellationToken cancelServiceInstance)
        {
            while (!cancelServiceInstance.IsCancellationRequested)
            {
                if (readOnly)
                {
                    await actor.PerformReadonlyAction();
                }
                else
                {
                    await actor.PerformReadWriteAction();
                }
                Interlocked.Increment(ref counter);
            }


        }

        async Task ProgressRunner(CancellationToken cancelServiceInstance)
        {
            var sw = new Stopwatch();
            sw.Start();

            var totalCounted = 0L;
            var totalElapsed = 0.0;

            // This partition's replica continues processing until the replica is terminated.
            while (!cancelServiceInstance.IsCancellationRequested)
            {
                await Task.Delay(1000, cancelServiceInstance);
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
    }
}
