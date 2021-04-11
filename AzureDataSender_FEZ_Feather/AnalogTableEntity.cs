using System;
using System.Collections;
using System.Text;
using System.Threading;
using RoSchmi.Net.Azure.Storage;


namespace AzureDataSender
{
    public class AnalogTableEntity : TableEntity
    {
        public string SampleTime { get; set; }
        public string T_1 { get; set; }
        public string T_2 { get; set; }
        public string T_3 { get; set; }
        public string T_4 { get; set; }

        // Your entity type must expose a parameter-less constructor
        public AnalogTableEntity() { }

        // Define the PK and RK
        public AnalogTableEntity(string partitionKey, string rowKey, ArrayList pProperties)
            : base(partitionKey, rowKey)
        {
            this.Properties = pProperties;    // store the ArrayList

            var myProperties = new PropertyClass()
            {
                PartitionKey = partitionKey,
                RowKey = rowKey,
                SampleTime = ((string[]) this.Properties[0])[2],
                T_1 = ((string[])this.Properties[1])[2],
                T_2 = ((string[])this.Properties[2])[2],
                T_3 = ((string[])this.Properties[3])[2],
                T_4 = ((string[])this.Properties[4])[2],
            };
        //  this.JsonString = JsonConverter.Serialize(myProperties).ToString();
        }

        private class PropertyClass
        {
            public string RowKey;
            public string PartitionKey;
            public string SampleTime;
            public string T_1;
            public string T_2;
            public string T_3;
            public string T_4;
        }
    }
}
