using System;
using System.Collections;
using RoSchmi.Net.Azure.Storage;

namespace AzureDataSender
{
    public static class AnalogTablePropertiesAL
    {
        public static ArrayList AnalogPropertiesAL(string pSampleTime, double pT_1, double pT_2, double pT_3, double pT_4)
        {
            TableEntityProperty property;
            ArrayList propertiesAL = new ArrayList();
            property = new TableEntityProperty("SampleTime", pSampleTime, "Edm.String");
            propertiesAL.Add(makePropertyArray.result(property));
            property = new TableEntityProperty("T_1", pT_1.ToString("f1"), "Edm.String");
            propertiesAL.Add(makePropertyArray.result(property));
            property = new TableEntityProperty("T_2", pT_2.ToString("f1"), "Edm.String");
            propertiesAL.Add(makePropertyArray.result(property));
            property = new TableEntityProperty("T_3", pT_3.ToString("f1"), "Edm.String");
            propertiesAL.Add(makePropertyArray.result(property));
            property = new TableEntityProperty("T_4", pT_4.ToString("f1"), "Edm.String");
            propertiesAL.Add(makePropertyArray.result(property));
            return propertiesAL;
        }

    }
}
