namespace STBootLib;

/// <summary>
/// command list.
/// </summary>
public enum STCmds
{
    /// <summary> This is used by bootloader to determine the baudrate. </summary>
    INIT = 0x7F,

    /// <summary>
    /// Gets the version and the allowed commands supported by
    /// the current version of the bootloader.
    /// </summary>
    GET = 0x00,

    /// <summary> Gets the bootloader version and the Read Protection status
    /// of the Flash memory.
    /// </summary>
    GET_PROT = 0x01,

    /// <summary> Gets the chip ID. </summary>
    GET_ID = 0x02,

    /// <summary> Reads up to 256 bytes of memory starting from an address specified by the application. </summary>
    READ = 0x11,

    /// <summary> Jumps to user application code located in the internal Flash memory or in SRAM. </summary>
    GO = 0x21,

    /// <summary>
    /// Writes up to 256 bytes to the RAM or Flash memory starting
    /// from an address specified by the application.
    /// </summary>
    WRITE = 0x31,

    /// <summary> Erases from one to all the Flash memory pages. </summary>
    ERASE = 0x43,

    /// <summary>
    /// Erases from one to all the Flash memory pages using
    /// two byte addressing mode (available only for v3.0 usart
    /// bootloader versions and above). </summary>
    EXT_ERASE = 0x44,

    /// <summary> Enables the write protection for some sectors. </summary>
    WR_PROTECT = 0x63,

    /// <summary> Disables the write protection for all Flash memory sectors. </summary>
    WR_UNPROTECT = 0x73,

    /// <summary> Enables the read protection. </summary>
    RD_PROTECT = 0x82,

    /// <summary> Disables the read protection. </summary>
    RD_UNPROTECT = 0x92,
}

/// <summary>
/// Special erase mode for normal erase command.
/// </summary>
public enum STEraseMode
{
    /// <summary>
    /// erase all sectors.
    /// </summary>
    GLOBAL = 0xff,
}

/// <summary>
/// special erase mode for normal erase command.
/// </summary>
public enum STExtendedEraseMode
{
    /// <summary> erase all sectors. </summary>
    GLOBAL = 0xffff,

    /// <summary> erase bank 1. </summary>
    BANK1 = 0xfffe,

    /// <summary> erase bank 2. </summary>
    BANK2 = 0xfffd,
}
