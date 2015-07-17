using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STBootLib
{
    /* response codes*/
    public enum STResps
    {
        /* command accepted */
        ACK = 0x79,
        /* command discarded */
        NACK = 0x1F,
    }
}
