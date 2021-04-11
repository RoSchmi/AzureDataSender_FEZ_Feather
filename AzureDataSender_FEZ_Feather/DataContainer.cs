using System;
using System.Text;
using AzureDataSender.Models;

namespace AzureDataSender_FEZ_Feather
{
    class DataContainer
    {
        private static readonly object LockProgram = new object();
        private TimeSpan dataInvalidateTime = new TimeSpan(0, 15, 0);

        AnalogValueSet[] analogValueSets = new AnalogValueSet[4];

        public DataContainer(TimeSpan pDataInvalidateTime)
        {

            for (int i = 1; i < 5; i++)
            {
                analogValueSets[i - 1] = new AnalogValueSet(i, DateTime.MinValue, 999.9);
            }
        }

        public bool SetNewAnalogValue(int pPropertyNumber, DateTime pSampleTime, double pMeasureValue)
        {
            if ((pPropertyNumber < 1) || (pPropertyNumber > 4))
            {
                return false;
            }
            else
            {
                lock (LockProgram)
                {                  
                    analogValueSets[pPropertyNumber -1] = new AnalogValueSet(pPropertyNumber, pSampleTime, pMeasureValue);
                    return true;
                }
            }
        }

        public AnalogValueSet GetAnalogValueSet(int pPropertyNumber)
        {
            lock (LockProgram)
            {
                AnalogValueSet returnSet = analogValueSets[pPropertyNumber - 1];
                // if the read value is outdated we return arbitrarily 999.9 (means value is not valid)
                // otherwise we return the value
                if ((DateTime.Now - returnSet.SampleTime) > dataInvalidateTime)
                {
                    return new AnalogValueSet(pPropertyNumber, returnSet.SampleTime, 999.9);
                }
                else
                {
                    return returnSet;
                }
            }
        }

        public TimeSpan DataInvalidateTime
        {
            get
            {
                return dataInvalidateTime;
            }
            set
            {
                dataInvalidateTime = value;
            }
        }
    }
}
