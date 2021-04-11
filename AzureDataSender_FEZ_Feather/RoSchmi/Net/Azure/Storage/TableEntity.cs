using System;
//using Microsoft.SPOT;
using System.Collections;
using System.Diagnostics;

namespace RoSchmi.Net.Azure.Storage
{
    public class TableEntity : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public string ETag { get; set; }
        public DateTime TimeStamp {get; set;}
        public static bool DisablePropertyResolverCache { get; set; }

        public string JsonString = string.Empty;

        public ArrayList Properties;

        public TableEntity() { }

        public TableEntity(string partitionKey, string rowKey)
        {
            this.PartitionKey = partitionKey;
            this.RowKey = rowKey;
        }

        public string ReadJson()
        {
            return JsonString;
        }

        public string ReadEntity()
        { return "Not implemented"; }

        public void WriteEntity(string Ausgabe)
        { Debug.WriteLine(Ausgabe); }

    }
}
