using System;
//using Microsoft.SPOT;
//using PervasiveDigital.Utilities;

namespace RoSchmi.Net.Azure.Storage
{
    public class TableEntityProperty
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Type { get; set; }


        public TableEntityProperty() { }
        public TableEntityProperty(string pName, string pValue, string pType)
        {
            this.Name = pName;
            this.Value = pValue;
            this.Type = pType;
        }

        public string[] propertyArray()
        {
            return new string[4] { String.Format("<d:{0} m:type=\"{2}\">{1}</d:{0}>", Name, Value, Type), this.Name, this.Value, this.Type };
            //return new string[4] { StringUtilities.Format("<d:{0} m:type=\"{2}\">{1}</d:{0}>", Name, Value, Type), this.Name, this.Value, this.Type };
        }
    }
}
