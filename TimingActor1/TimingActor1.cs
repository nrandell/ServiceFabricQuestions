using TimingActor1.Interfaces;
using Microsoft.ServiceFabric.Actors;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TimingActor1
{
    /// <remarks>
    /// Each ActorID maps to an instance of this class.
    /// The IProjName  interface (in a separate DLL that client code can
    /// reference) defines the operations exposed by ProjName objects.
    /// </remarks>
    internal class TimingActor1 : StatefulActor<TimingActor1.ActorState>, ITimingActor1
    {
        [DataContract]
        internal sealed class ActorState
        {
            [DataMember]
            public int Count { get; set; }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "TimingActor1.ActorState[Count = {0}]", Count);
            }
        }

        /// <summary>
        /// This method is called whenever an actor is activated.
        /// </summary>
        protected override Task OnActivateAsync()
        {
            if (this.State == null)
            {
                // This is the first time this actor has ever been activated.
                // Set the actor's initial state values.
                this.State = new ActorState { Count = 0 };
            }

            ActorEventSource.Current.ActorMessage(this, "State initialized to {0}", this.State);
            return Task.FromResult(true);
        }


        [Readonly]
        public Task<int> PerformReadonlyAction()
        {
            return Task.FromResult(State.Count);

        }

        public Task<int> PerformReadWriteAction()
        {
            State.Count += 1;
            return Task.FromResult(State.Count);
        }
    }
}
