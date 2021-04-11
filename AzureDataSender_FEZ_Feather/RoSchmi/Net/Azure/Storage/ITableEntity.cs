using System;
using System.Collections;

namespace RoSchmi.Net.Azure.Storage
{
    public interface ITableEntity
    {
        string ETag { get; set; }
        string PartitionKey { get; set; }
        string RowKey { get; set;}
        DateTime TimeStamp { get; set; }

        string ReadJson();

        string ReadEntity();

        void WriteEntity(string Ausgabe);
    }
}
