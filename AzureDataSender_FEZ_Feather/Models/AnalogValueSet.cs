using System;


namespace AzureDataSender.Models
{
    public class AnalogValueSet
    {
        public AnalogValueSet() { }

        public AnalogValueSet(int pPropertyNumber, DateTime pSampleTime, double pMeasureValue)
        {
            this.PropertyNumber = pPropertyNumber;
            this.SampleTime = pSampleTime;
            this.MeasureValue = pMeasureValue;
        }

        public double MeasureValue { get; set; }
        public DateTime SampleTime { get; set; }        
        public int PropertyNumber { get; set; }       
    }
}
