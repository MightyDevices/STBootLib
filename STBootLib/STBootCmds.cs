using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STBootLib
{
    /* command list */
    public enum STCmds
    {
        /* this is used by bootloader to determine the baudrate */
        INIT = 0x7F,
        /* Gets the version and the allowed commands supported 
         * by the current version of the bootloader */
        GET = 0x00,
        /* Gets the bootloader version and the Read Protection 
         * status of the Flash memory */
        GET_PROT = 0x01,
        /* Gets the chip ID */
        GET_ID = 0x02,
        /* Reads up to 256 bytes of memory starting from an 
         * address specified by the application */
        READ = 0x11,
        /* Jumps to user application code located in the internal 
         * Flash memory or in SRAM */
        GO = 0x21,
        /* Writes up to 256 bytes to the RAM or Flash memory starting 
         * from an address specified by the application */
        WRITE = 0x31,
        /* Erases from one to all the Flash memory pages */
        ERASE = 0x43,
        /* Erases from one to all the Flash memory pages using 
         * two byte addressing mode (available only for v3.0 usart 
         * bootloader versions and above). */
        EXT_ERASE = 0x44,
        /* Enables the write protection for some sectors */
        WR_PROTECT = 0x63,
        /* Disables the write protection for all Flash memory sectors */
        WR_UNPROTECT = 0x73,
        /* Enables the read protection */
        RD_PROTECT = 0x82,
        /* Disables the read protection */
        RD_UNPROTECT = 0x92
    }

    /* special erase mode for normal erase command */
    public enum STEraseMode
    {
        /* erase all sectors */
        GLOBAL = 0xff,
    }

    /* special erase mode for normal erase command */
    public enum STExtendedEraseMode
    {
        /* erase all sectors */
        GLOBAL = 0xffff,
        /* erase bank 1 */
        BANK1 = 0xfffe,
        /* erase bank 2 */
        BANK2 = 0xfffd
    }
}
