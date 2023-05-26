using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace STBootLib;

/// <summary>
/// Boot.
/// </summary>
public class STBoot : IDisposable
{
    // command mutex
    private readonly SemaphoreSlim sem;

    private SerialPort serialPort;

    /// <summary>
    /// Initializes a new instance of the <see cref="STBoot"/> class.
    /// </summary>
    public STBoot()
    {
        this.Commands = new List<STCmds>();
        this.sem = new SemaphoreSlim(1);
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="STBoot"/> class.
    /// </summary>
    ~STBoot()
    {
        this.Dispose();
    }

    /// <summary>
    /// Gets the list of supported commands.
    /// </summary>
    public List<STCmds> Commands { get; private set; }

    /// <summary>
    /// Gets the version.
    /// </summary>
    public string Version { get; private set; }

    /// <summary>
    /// Gets the product id.
    /// </summary>
    public ushort ProductId { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        // close serial port
        this.Close();
    }

    /// <summary>
    /// Open serial port.
    /// </summary>
    /// <param name="portName">The port name.</param>
    /// <param name="baudRate">The baud rate.</param>
    public void Open(string portName, uint baudRate)
    {
        this.serialPort = new SerialPort(portName, (int)baudRate, Parity.Even, 8);
        this.serialPort.Open();

        this.serialPort.DiscardInBuffer();
        this.serialPort.DiscardOutBuffer();
    }

    /// <summary>
    /// Close serial port.
    /// </summary>
    public void Close()
    {
        // close permitted?
        if (this.serialPort != null && this.serialPort.IsOpen)
        {
            this.serialPort.Close();
        }
    }

    /// <summary>
    /// Initialize communication.
    /// </summary>
    /// <returns>The async task.</returns>
    public async Task Initialize()
    {
        // perform autobauding
        await this.Init();

        // get version and command list
        await this.Get();

        // no support for get id?
        if (!this.Commands.Contains(STCmds.GET_ID))
        {
            throw new STBootException("Command not supported");
        }

        await this.GetId();
    }

    /// <summary>
    /// unprotect memory.
    /// </summary>
    /// <returns>The async task.</returns>
    public async Task Unprotect()
    {
        // no support for unprotect?
        if (!this.Commands.Contains(STCmds.WR_UNPROTECT))
        {
            throw new STBootException("Command not supported");
        }

        // no support for unprotect?
        if (!this.Commands.Contains(STCmds.RD_UNPROTECT))
        {
            throw new STBootException("Command not supported");
        }

        await this.ReadUnprotect();
        await this.WriteUnprotect();
    }

    /// <summary>
    /// Read memory.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="buf">The buffer.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="size">The size.</param>
    /// <param name="p">The progress.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The async task.</returns>
    public async Task ReadMemory(
        uint address,
        byte[] buf,
        int offset,
        int size,
        IProgress<int> p,
        CancellationToken ct)
    {
        // no support for read?
        if (!this.Commands.Contains(STCmds.READ))
        {
            throw new STBootException("Command not supported");
        }

        // data is read in chunks
        var bytesRead = 0;
        while (size > 0 && !ct.IsCancellationRequested)
        {
            var chunkSize = Math.Min(size, 256);

            // read a single chunk
            await this.Read(address, buf, offset, chunkSize);

            // update iterators
            size -= chunkSize;
            offset += chunkSize;
            address += (uint)chunkSize;

            // update number of bytes read
            bytesRead += chunkSize;

            p?.Report(bytesRead);
        }

        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("Read cancelled");
        }
    }

    /// <summary>
    /// Write memory.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="buf">The buffer.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="size">The size.</param>
    /// <param name="p">The progress.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The async task.</returns>
    public async Task WriteMemory(
        uint address,
        byte[] buf,
        int offset,
        int size,
        IProgress<STBootProgress> p,
        CancellationToken ct)
    {
        // no support for read?
        if (!this.Commands.Contains(STCmds.WRITE))
        {
            throw new STBootException("Command not supported");
        }

        // data is read in chunks
        var bytesWritten = 0;
        var bytesTotal = size;
        while (size > 0 && !ct.IsCancellationRequested)
        {
            var chunkSize = Math.Min(size, 256);

            // read a single chunk
            await this.Write(address, buf, offset, chunkSize);

            // update iterators
            size -= chunkSize;
            offset += chunkSize;
            address += (uint)chunkSize;
            bytesWritten += chunkSize;

            p?.Report(new STBootProgress(bytesWritten, bytesTotal));
        }

        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("Write cancelled");
        }
    }

    /// <summary>
    /// Erase page.
    /// </summary>
    /// <param name="pageNumber">The page number.</param>
    /// <returns>The async task.</returns>
    public async Task ErasePage(uint pageNumber)
    {
        // 'classic' erase operation supported?
        if (this.Commands.Contains(STCmds.ERASE))
        {
            await this.Erase(pageNumber);
        }

        // 'extended' erase operation supported?
        else if (this.Commands.Contains(STCmds.EXT_ERASE))
        {
            await this.ExtendedErase(pageNumber);
        }

        // no operation supported
        else
        {
            throw new STBootException("Command not supported");
        }
    }

    /// <summary>
    /// Perform global erase.
    /// </summary>
    /// <returns>The async task.</returns>
    public async Task GlobalErase()
    {
        // 'classic' erase operation supported?
        if (this.Commands.Contains(STCmds.ERASE))
        {
            await this.EraseSpecial(STEraseMode.GLOBAL);
        }

        // 'extended' erase operation supported?
        else if (this.Commands.Contains(STCmds.EXT_ERASE))
        {
            await this.ExtendedEraseSpecial(STExtendedEraseMode.GLOBAL);
        }

        // no operation supported
        else
        {
            throw new STBootException("Command not supported");
        }
    }

    /// <summary>
    /// Jump to user code.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The async task.</returns>
    public async Task Jump(uint address)
    {
        // no support for go?
        if (!this.Commands.Contains(STCmds.GO))
        {
            throw new STBootException("Command not supported");
        }

        await this.Go(address);
    }

    private async Task Init()
    {
        // command word
        var tx = new byte[1];

        // response code
        var ack = new byte[1];

        // store code
        tx[0] = (byte)STCmds.INIT;

        // wait for command sender to finish its job with previous command
        await this.sem.WaitAsync();

        // try to send command and wait for response
        try
        {
            await this.SerialWrite(tx, 0, tx.Length);
            await this.SerialRead(ack, 0, 1);

            if (ack[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Command Rejected");
            }
        }
        catch (Exception)
        {
            this.sem.Release();
            throw;
        }

        this.sem.Release();
    }

    private async Task Get()
    {
        // command word
        var tx = new byte[2];

        // temporary storage for response bytes
        var tmp = new byte[1];

        // number or response bytes
        int nbytes;

        // rx buffer
        byte[] rx;

        // store code
        tx[0] = (byte)STCmds.GET;

        // set checksum
        tx[1] = this.ComputeChecksum(tx, 0, 1);

        // wait for command sender to finish its job with previous command
        await this.sem.WaitAsync();

        // try to send command and wait for response
        try
        {
            await this.SerialWrite(tx, 0, tx.Length);

            await this.SerialRead(tmp, 0, 1);

            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Command Rejected");
            }

            // wait for number of bytes
            await this.SerialRead(tmp, 0, 1);

            // assign number of bytes that will follow (add for acks)
            nbytes = tmp[0] + 2;

            // nbytes must be equal to 13 for stm32 products
            if (nbytes != 13)
            {
                throw new STBootException("Invalid length");
            }

            rx = new byte[nbytes];
            await this.SerialRead(rx, 0, rx.Length);
        }
        catch (Exception)
        {
            this.sem.Release();
            throw;
        }

        // store version information
        this.Version = (rx[0] >> 4).ToString() + "." +
                       (rx[0] & 0xf).ToString();

        this.Commands = new List<STCmds>();
        for (var i = 1; i < nbytes - 1; i++)
        {
            this.Commands.Add((STCmds)rx[i]);
        }

        this.sem.Release();
    }

    private async Task GetId()
    {
        // command word
        var tx = new byte[2];

        // temporary storage for response bytes
        var tmp = new byte[1];

        // number or response bytes
        int nbytes;

        // rx buffer
        byte[] rx;

        // store code
        tx[0] = (byte)STCmds.GET_ID;

        // set checksum
        tx[1] = this.ComputeChecksum(tx, 0, 1);

        // try to send command and wait for response
        try
        {
            await this.SerialWrite(tx, 0, tx.Length);

            await this.SerialRead(tmp, 0, 1);

            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Command Rejected");
            }

            // wait for number of bytes
            await this.SerialRead(tmp, 0, 1);

            // assign number of bytes that will follow (add for acks)
            nbytes = tmp[0] + 2;

            // nbytes must be equal to 3 for stm32 products
            if (nbytes != 3)
            {
                throw new STBootException("Invalid length");
            }

            rx = new byte[nbytes];
            await this.SerialRead(rx, 0, rx.Length);
        }
        catch (Exception)
        {
            this.sem.Release();
            throw;
        }

        // store product id
        this.ProductId = (ushort)(rx[0] << 8 | rx[1]);

        this.sem.Release();
    }

    private async Task Read(uint address, byte[] buf, int offset, int length)
    {
        // command word
        var tx = new byte[9];

        // temporary storage for response bytes
        var tmp = new byte[1];

        // command code
        tx[0] = (byte)STCmds.READ;

        // checksum
        tx[1] = this.ComputeChecksum(tx, 0, 1);

        // store address
        tx[2] = (byte)((address >> 24) & 0xff);
        tx[3] = (byte)((address >> 16) & 0xff);
        tx[4] = (byte)((address >> 8) & 0xff);
        tx[5] = (byte)(address & 0xff);

        // address checksum (needs to be not negated. why? because ST! that's why.
        tx[6] = (byte)~this.ComputeChecksum(tx, 2, 4);

        // store number of bytes
        tx[7] = (byte)(length - 1);

        // size checksum
        tx[8] = this.ComputeChecksum(tx, 7, 1);

        // try to send command and wait for response
        try
        {
            await this.SerialWrite(tx, 0, 2);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Command Rejected");
            }

            // send address
            await this.SerialWrite(tx, 2, 5);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Address Rejected");
            }

            // send address
            await this.SerialWrite(tx, 7, 2);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Size Rejected");
            }

            await this.SerialRead(buf, offset, length);
        }
        catch (Exception)
        {
            this.sem.Release();
            throw;
        }

        this.sem.Release();
    }

    private async Task Go(uint address)
    {
        // command word
        var tx = new byte[7];

        // temporary storage for response bytes
        var tmp = new byte[1];

        // command code
        tx[0] = (byte)STCmds.GO;

        // checksum
        tx[1] = this.ComputeChecksum(tx, 0, 1);

        // store address
        tx[2] = (byte)((address >> 24) & 0xff);
        tx[3] = (byte)((address >> 16) & 0xff);
        tx[4] = (byte)((address >> 8) & 0xff);
        tx[5] = (byte)(address & 0xff);

        // address checksum (needs to be not negated. why? because ST! that's why.
        tx[6] = (byte)~this.ComputeChecksum(tx, 2, 4);

        // try to send command and wait for response
        try
        {
            await this.SerialWrite(tx, 0, 2);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Command Rejected");
            }

            await this.SerialWrite(tx, 2, 5);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Address Rejected");
            }
        }
        catch (Exception)
        {
            this.sem.Release();
            throw;
        }

        this.sem.Release();
    }

    private async Task Write(uint address, byte[] data, int offset, int length)
    {
        // command word
        var tx = new byte[9];

        // temporary storage for response bytes
        var tmp = new byte[1];

        // command code
        tx[0] = (byte)STCmds.WRITE;

        // checksum
        tx[1] = this.ComputeChecksum(tx, 0, 1);

        // store address
        tx[2] = (byte)((address >> 24) & 0xff);
        tx[3] = (byte)((address >> 16) & 0xff);
        tx[4] = (byte)((address >> 8) & 0xff);
        tx[5] = (byte)(address & 0xff);

        // address checksum (needs to be not negated. why? because ST! that's why.
        tx[6] = (byte)~this.ComputeChecksum(tx, 2, 4);

        // number of bytes
        tx[7] = (byte)(length - 1);

        // data checksum
        tx[8] = (byte)(~(this.ComputeChecksum(data, offset, length) ^ tx[7]));

        // try to send command and wait for response
        try
        {
            await this.SerialWrite(tx, 0, 2);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Command Rejected");
            }

            await this.SerialWrite(tx, 2, 5);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Address Rejected");
            }

            await this.SerialWrite(tx, 7, 1);
            await this.SerialWrite(data, offset, length);
            await this.SerialWrite(tx, 8, 1);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Data Rejected");
            }
        }
        catch (Exception)
        {
            this.sem.Release();
            throw;
        }

        this.sem.Release();
    }

    private async Task Erase(uint pageNumber)
    {
        // command word
        var tx = new byte[5];

        // temporary storage for response bytes
        var tmp = new byte[1];

        // command code
        tx[0] = (byte)STCmds.ERASE;

        // checksum
        tx[1] = this.ComputeChecksum(tx, 0, 1);

        // erase single page
        tx[2] = 0;

        // set page number
        tx[3] = (byte)pageNumber;

        // checksum
        tx[4] = (byte)~this.ComputeChecksum(tx, 2, 2);

        // try to send command and wait for response
        try
        {
            await this.SerialWrite(tx, 0, 2);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Command Rejected");
            }

            await this.SerialWrite(tx, 2, 3);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Page Rejected");
            }
        }
        catch (Exception)
        {
            this.sem.Release();
            throw;
        }

        this.sem.Release();
    }

    private async Task EraseSpecial(STEraseMode mode)
    {
        // command word
        var tx = new byte[4];

        // temporary storage for response bytes
        var tmp = new byte[1];

        // command code
        tx[0] = (byte)STCmds.ERASE;

        // checksum
        tx[1] = this.ComputeChecksum(tx, 0, 1);

        // erase single page
        tx[2] = (byte)((int)mode);

        // checksum
        tx[3] = (byte)~this.ComputeChecksum(tx, 2, 2);

        // try to send command and wait for response
        try
        {
            await this.SerialWrite(tx, 0, 2);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Command Rejected");
            }

            await this.SerialWrite(tx, 2, 2);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Special Code Rejected");
            }
        }
        catch (Exception)
        {
            this.sem.Release();
            throw;
        }

        this.sem.Release();
    }

    private async Task ExtendedErase(uint pageNumber)
    {
        // command word
        var tx = new byte[7];

        // temporary storage for response bytes
        var tmp = new byte[1];

        // command code
        tx[0] = (byte)STCmds.EXT_ERASE;

        // checksum
        tx[1] = this.ComputeChecksum(tx, 0, 1);

        // erase single page
        tx[2] = 0;
        tx[3] = 0;

        // set page number
        tx[4] = (byte)(pageNumber >> 8);
        tx[5] = (byte)(pageNumber >> 0);

        // checksum
        tx[6] = (byte)~this.ComputeChecksum(tx, 2, 5);

        // try to send command and wait for response
        try
        {
            await this.SerialWrite(tx, 0, 2);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Command Rejected");
            }

            await this.SerialWrite(tx, 2, 5);

            // wait for response code. use longer timeout, erase might
            // take a while or two.
            await this.SerialRead(tmp, 0, 1, 3000);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Page Rejected");
            }
        }
        catch (Exception)
        {
            this.sem.Release();
            throw;
        }

        this.sem.Release();
    }

    private async Task ExtendedEraseSpecial(STExtendedEraseMode mode)
    {
        // command word
        var tx = new byte[5];

        // temporary storage for response bytes
        var tmp = new byte[1];

        // command code
        tx[0] = (byte)STCmds.EXT_ERASE;

        // checksum
        tx[1] = this.ComputeChecksum(tx, 0, 1);

        // erase single page
        tx[2] = (byte)((int)mode >> 8);
        tx[3] = (byte)((int)mode >> 0);

        // checksum
        tx[4] = (byte)~this.ComputeChecksum(tx, 2, 3);

        // try to send command and wait for response
        try
        {
            await this.SerialWrite(tx, 0, 2);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Command Rejected");
            }

            await this.SerialWrite(tx, 2, 3);

            // wait for response code. use longer timeout, erase might take a while or two.
            await this.SerialRead(tmp, 0, 1, 10000);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Special code Rejected");
            }
        }
        catch (Exception)
        {
            this.sem.Release();
            throw;
        }

        this.sem.Release();
    }

    private async Task WriteUnprotect()
    {
        // command word
        var tx = new byte[2];

        // temporary storage for response bytes
        var tmp = new byte[1];

        // command code
        tx[0] = (byte)STCmds.WR_UNPROTECT;

        // checksum
        tx[1] = this.ComputeChecksum(tx, 0, 1);

        // try to send command and wait for response
        try
        {
            await this.SerialWrite(tx, 0, 2);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Command Rejected");
            }

            // wait for response code. use longer timeout, erase might
            // take a while or two.
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Write Unprotect Rejected");
            }
        }
        finally
        {
            this.sem.Release();
        }
    }

    private async Task ReadUnprotect()
    {
        // command word
        var tx = new byte[2];

        // temporary storage for response bytes
        var tmp = new byte[1];

        // command code
        tx[0] = (byte)STCmds.RD_UNPROTECT;

        // checksum
        tx[1] = this.ComputeChecksum(tx, 0, 1);

        // try to send command and wait for response
        try
        {
            await this.SerialWrite(tx, 0, 2);
            await this.SerialRead(tmp, 0, 1);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Command Rejected");
            }

            // wait for response code. use longer timeout, erase might
            // take a while or two.
            await this.SerialRead(tmp, 0, 10000);
            if (tmp[0] != (byte)STResps.ACK)
            {
                throw new STBootException("Write Unprotect Rejected");
            }
        }
        finally
        {
            this.sem.Release();
        }
    }

    private byte ComputeChecksum(byte[] data, int offset, int count)
    {
        byte xor = 0xff;

        for (var i = offset; i < count + offset; i++)
        {
            xor ^= data[i];
        }

        return xor;
    }

    private async Task SerialWrite(byte[] data, int offset, int count)
    {
        await this.serialPort.BaseStream.WriteAsync(data, offset, count);
    }

    /// <summary>
    /// Standard read with timeout equal to 1s.
    /// </summary>
    private async Task SerialRead(byte[] data, int offset, int count)
    {
        await this.SerialRead(data, offset, count, 1000);
    }

    /// <summary>
    /// Read 'length' number of bytes from serial port.
    /// </summary>
    private async Task SerialRead(byte[] data, int offset, int count, int timeout)
    {
        var baseStream = this.serialPort.BaseStream;
        var bytesRead = 0;

        // read until all bytes are fetched from serial port
        while (bytesRead < count)
        {
            // this try is for timeout handling
            try
            {
                // prepare task
                bytesRead += await baseStream.ReadAsync(data, offset + bytesRead, count - bytesRead)
                    .WithTimeout(timeout);
            }
            catch (OperationCanceledException)
            {
                throw new STBootException("Timeout");
            }
        }
    }
}
