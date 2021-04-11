using System;
using System.Collections;
using System.Text;
using System.Threading;
using RoSchmi.Net.Azure.Storage;
//using PervasiveDigital.Json;

namespace AzureDataSender
{
    public class OnOffTableEntity : TableEntity
    {
        
        public string ActStatus { get; set; }
        public string LastStatus { get; set; }
        public string OnTimeDay { get; set; }
        public string SampleTime { get; set; }
        public string TimeFromLast { get; set; }

        // Your entity type must expose a parameter-less constructor
        public OnOffTableEntity() { }

        // Define the PK and RK
        public OnOffTableEntity(string partitionKey, string rowKey, ArrayList pProperties)
            : base(partitionKey, rowKey)
        {
            this.Properties = pProperties;    // store the ArrayList

            var myProperties = new PropertyClass()
            {
                PartitionKey = partitionKey,
                RowKey = rowKey,
                ActStatus = ((string[])this.Properties[0])[2],
                LastStatus = ((string[])this.Properties[1])[2],
                OnTimeDay = ((string[])this.Properties[2])[2],
                //SampleTime = ((string[])this.Properties[3])[2],

                TimeFromLast = ((string[])this.Properties[4])[2],
                SampleTime = ((string[])this.Properties[3])[2]
            };
            //  this.JsonString = JsonConverter.Serialize(myProperties).ToString();
        }

        private class PropertyClass
        {
            public string RowKey;
            public string PartitionKey;
            public string ActStatus;
            public string LastStatus;
            public string SampleTime;
            public string OnTimeDay;
            public string TimeFromLast;            
        }
    }
}


