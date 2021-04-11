using System;
using System.Collections;
using System.Text;
using System.Threading;
using RoSchmi.Net.Azure.Storage;

namespace AzureDataSender
{
    class OnOffTablePropertiesAL
    {       
        public static ArrayList OnOffPropertiesAL(string ActStatus, string LastStatus, string OnTimeDay, string SampleTime, string TimeFromLast)
        {
            TableEntityProperty property;
            ArrayList propertiesAL = new ArrayList();
            property = new TableEntityProperty("ActStatus", ActStatus, "Edm.String");
            propertiesAL.Add(makePropertyArray.result(property));
            property = new TableEntityProperty("LastStatus", LastStatus, "Edm.String");
            propertiesAL.Add(makePropertyArray.result(property));
            property = new TableEntityProperty("OnTimeDay", OnTimeDay, "Edm.String");
            propertiesAL.Add(makePropertyArray.result(property));
            property = new TableEntityProperty("SampleTime", SampleTime, "Edm.String");
            propertiesAL.Add(makePropertyArray.result(property));
            property = new TableEntityProperty("TimeFromLast", TimeFromLast, "Edm.String");
            propertiesAL.Add(makePropertyArray.result(property));
            return propertiesAL;
        }

    }
}
