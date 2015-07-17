using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STBootLib
{
    public class STBootProgress
    {
        /* total number of bytes */
        public readonly int bytesTotal;
        /* number of bytes processed */
        public readonly int bytesProcessed;

        public STBootProgress(int bytesProcessed, int bytesTotal)
        {
            /* set the number of bytes processed */
            this.bytesProcessed = bytesProcessed;
            /* set the total number of bytes */
            this.bytesTotal = bytesTotal;
        }
    }
}
