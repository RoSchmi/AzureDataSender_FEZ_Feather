using System;

namespace RoSchmi.Net.Azure.Storage
{
    public static class makePropertyArray
    {
        public static string[] result(TableEntityProperty property)
        {
            return new string[] { property.propertyArray()[0], property.propertyArray()[1], property.propertyArray()[2], property.propertyArray()[3] };
        }
    }
}

