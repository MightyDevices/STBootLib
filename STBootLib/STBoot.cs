using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Ports;

namespace STBootLib
{
    public class STBoot : IDisposable
    {
        /* serial port */
        SerialPort sp;
        /* command mutex */
        SemaphoreSlim sem;
        /* list of supported commands */
        List<STCmds> Commands;


        /* bootloader version */
        public string Version;
        /* product id */
        public ushort ProductID;
        

        /* constructor */
        public STBoot()
        {
            Commands = new List<STCmds>();
            /* initialize mutex */
            sem = new SemaphoreSlim(1);
        }

        /* destructor */
        ~STBoot()
        {
            /* dispose of serial port */
            Dispose();
        }

        /* dispose implementation */
        public void Dispose()
        {
            /* close serial port */
            Close();
        }

        /* open serial port */
        public void Open(string portName, uint baudRate)
        {
            /* initialize serial port */
            sp = new SerialPort(portName, (int)baudRate, Parity.Even, 8);
            /* open serial port */
            sp.Open();

            /* discard buffers */
            sp.DiscardInBuffer();
            sp.DiscardOutBuffer();
        }

        /* close */
        public void Close()
        {
            /* close permitted? */
            if (sp != null && sp.IsOpen)
                sp.Close();
        }

        /* initialize communication */
        public async Task Initialize()
        {
            /* perform autobauding */
            await Init();
            /* get version and command list */
            await Get();

            /* no support for get id? */
            if (!Commands.Contains(STCmds.GET_ID)) {
                /* throw an exception */
                throw new STBootException("Command not supported");
            }

            /* get product id */
            await GetID();
        }

        /* unprotect memory */
        public async Task Unprotect()
        {
            /* no support for unprotect? */
            if (!Commands.Contains(STCmds.WR_UNPROTECT))
                throw new STBootException("Command not supported");

            /* no support for unprotect? */
            if (!Commands.Contains(STCmds.RD_UNPROTECT))
                throw new STBootException("Command not supported");

            await ReadUnprotect();
            await WriteUnprotect();
        }

        /* read memory */
        public async Task ReadMemory(uint address, byte[] buf, int offset, 
            int size, IProgress<int> p, CancellationToken ct)
        {
            /* number of bytes read */
            int bread = 0;
            /* no support for read? */
            if (!Commands.Contains(STCmds.READ))
                throw new STBootException("Command not supported");

            /* data is read in chunks */
            while (size > 0 && !ct.IsCancellationRequested) {
                /* chunk size */
                int csize = Math.Min(size, 256);
                /* read a single chunk */
                await Read(address, buf, offset, csize);
         
                /* update iterators */
                size -= csize; offset += csize; address += (uint)csize; 
                /* update number of bytes read */
                bread += csize;

                /* report progress */
                if (p != null)
                    p.Report(bread);
            }

            /* throw exception if operation was cancelled */
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException("Read cancelled");
        }

        /* write memory */
        public async Task WriteMemory(uint address, byte[] buf, int offset,
            int size, IProgress<STBootProgress> p, CancellationToken ct)
        {
            /* number of bytes written */
            int bwritten = 0, btotal = size;

            /* no support for read? */
            if (!Commands.Contains(STCmds.WRITE))
                throw new STBootException("Command not supported");

            /* data is read in chunks */
            while (size > 0 && !ct.IsCancellationRequested) {
                /* chunk size */
                int csize = Math.Min(size, 256);
                /* read a single chunk */
                await Write(address, buf, offset, csize);

                /* update iterators */
                size -= csize; offset += csize; address += (uint)csize;
                /* update number of bytes read */
                bwritten += csize;

                /* report progress */
                if (p != null)
                    p.Report(new STBootProgress(bwritten, btotal));
            }

            /* throw exception if operation was cancelled */
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException("Write cancelled");
        }

        /* erase page */
        public async Task ErasePage(uint pageNumber)
        {
            /* 'classic' erase operation supported? */
            if (Commands.Contains(STCmds.ERASE)) {
                await Erase(pageNumber);
            /* 'extended' erase operation supported? */
            } else if (Commands.Contains(STCmds.EXT_ERASE)) {
                await ExtendedErase(pageNumber);
            /* no operation supported */
            } else {
                throw new STBootException("Command not supported");
            }
        }

        /* perform global erase */
        public async Task GlobalErase()
        {
            /* 'classic' erase operation supported? */
            if (Commands.Contains(STCmds.ERASE)) {
                await EraseSpecial(STEraseMode.GLOBAL);
                /* 'extended' erase operation supported? */
            } else if (Commands.Contains(STCmds.EXT_ERASE)) {
                await ExtendedEraseSpecial(STExtendedEraseMode.GLOBAL);
                /* no operation supported */
            } else {
                throw new STBootException("Command not supported");
            }
        }

        /* jump to user code */
        public async Task Jump(uint address)
        {
            /* no support for go? */
            if (!Commands.Contains(STCmds.GO))
                throw new STBootException("Command not supported");

            /* go! */
            await Go(address);
        }

        /* init */
        private async Task Init()
        {
            /* command word */
            var tx = new byte[1];
            /* response code */
            var ack = new byte[1];

            /* store code */
            tx[0] = (byte)STCmds.INIT;

            /* wait for command sender to finish its job with previous 
             * command */
            await sem.WaitAsync();

            /* try to send command and wait for response */
            try {
                /* send bytes */
                await SerialWrite(tx, 0, tx.Length);

                /* wait for response code */
                await SerialRead(ack, 0, 1);
               /* check response code */
                if (ack[0] != (byte)STResps.ACK)
                    throw new STBootException("Command Rejected");
            /* error during send */
            } catch (Exception) {
                /* release semaphore */
                sem.Release();
                /* re-throw */
                throw;
            }

            /* release semaphore */
            sem.Release();
        }

        /* get command */
        private async Task Get()
        {
            /* command word */
            var tx = new byte[2];
            /* temporary storage for response bytes */
            var tmp = new byte[1];
            /* numbe or response bytes */
            int nbytes;
            /* rx buffer */
            byte[] rx;
           
            /* store code */
            tx[0] = (byte)STCmds.GET;
            /* set checksum */
            tx[1] = ComputeChecksum(tx, 0, 1);

            /* wait for command sender to finish its job with previous 
             * command */
            await sem.WaitAsync();

            /* try to send command and wait for response */
            try {
                /* send bytes */
                await SerialWrite(tx, 0, tx.Length);

                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK) 
                    throw new STBootException("Command Rejected");

                /* wait for number of bytes */
                await SerialRead(tmp, 0, 1);
                /* assign number of bytes that will follow (add for acks) */
                nbytes = tmp[0] + 2;
                /* nbytes must be equal to 13 for stm32 products */
                if (nbytes != 13)
                    throw new STBootException("Invalid length");

                /* prepare buffer */
                rx = new byte[nbytes];
                /* receive response */
                await SerialRead(rx, 0, rx.Length);
            /* oops, something baaad happened! */
            } catch (Exception) {
                /* release semaphore */
                sem.Release();
                /* re-throw */
                throw;
            }

            /* store version information */
            Version = (rx[0] >> 4).ToString() + "." + 
                (rx[0] & 0xf).ToString();

            /* initialize command list */
            Commands = new List<STCmds>();
            /* add all commands */
            for (int i = 1; i < nbytes - 1; i++)
                Commands.Add((STCmds)rx[i]);

            /* release semaphore */
            sem.Release();
        }

        /* get id command */
        private async Task GetID()
        {
            /* command word */
            var tx = new byte[2];
            /* temporary storage for response bytes */
            var tmp = new byte[1];
            /* numbe or response bytes */
            int nbytes;
            /* rx buffer */
            byte[] rx;

            /* store code */
            tx[0] = (byte)STCmds.GET_ID;
            /* set checksum */
            tx[1] = ComputeChecksum(tx, 0, 1);

            /* try to send command and wait for response */
            try {
                /* send bytes */
                await SerialWrite(tx, 0, tx.Length);

                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Command Rejected");

                /* wait for number of bytes */
                await SerialRead(tmp, 0, 1);
                /* assign number of bytes that will follow (add for acks) */
                nbytes = tmp[0] + 2;
                /* nbytes must be equal to 3 for stm32 products */
                if (nbytes != 3)
                    throw new STBootException("Invalid length");

                /* prepare buffer */
                rx = new byte[nbytes];
                /* receive response */
                await SerialRead(rx, 0, rx.Length);
            /* oops, something baaad happened! */
            } catch (Exception) {
                /* release semaphore */
                sem.Release();
                /* re-throw */
                throw;
            }

            /* store product id */
            ProductID = (ushort)(rx[0] << 8 | rx[1]);

            /* release semaphore */
            sem.Release();
        }

        /* read command */
        private async Task Read(uint address, byte[] buf, int offset, int length)
        {
            /* command word */
            var tx = new byte[9];
            /* temporary storage for response bytes */
            var tmp = new byte[1];

            /* command code */
            tx[0] = (byte)STCmds.READ;
            /* checksum */
            tx[1] = ComputeChecksum(tx, 0, 1);

            /* store address */
            tx[2] = (byte)((address >> 24) & 0xff);
            tx[3] = (byte)((address >> 16) & 0xff);
            tx[4] = (byte)((address >> 8) & 0xff);
            tx[5] = (byte)(address & 0xff);
            /* address checksum (needs to be not negated. why? because ST! 
             * that's why. */
            tx[6] = (byte)~ComputeChecksum(tx, 2, 4);

            /* store number of bytes */
            tx[7] = (byte)(length - 1);
            /* size checksum */
            tx[8] = ComputeChecksum(tx, 7, 1);

            /* try to send command and wait for response */
            try {
                /* send bytes */
                await SerialWrite(tx, 0, 2);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Command Rejected");

                /* send address */
                await SerialWrite(tx, 2, 5);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Address Rejected");

                /* send address */
                await SerialWrite(tx, 7, 2);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Size Rejected");

                /* receive response */
                await SerialRead(buf, offset, length);
                /* oops, something baaad happened! */
            } catch (Exception) {
                /* release semaphore */
                sem.Release();
                /* re-throw */
                throw;
            }

            /* release semaphore */
            sem.Release();
        }

        /* go command */
        private async Task Go(uint address)
        {
            /* command word */
            var tx = new byte[7];
            /* temporary storage for response bytes */
            var tmp = new byte[1];

            /* command code */
            tx[0] = (byte)STCmds.GO;
            /* checksum */
            tx[1] = ComputeChecksum(tx, 0, 1);

            /* store address */
            tx[2] = (byte)((address >> 24) & 0xff);
            tx[3] = (byte)((address >> 16) & 0xff);
            tx[4] = (byte)((address >> 8) & 0xff);
            tx[5] = (byte)(address & 0xff);
            /* address checksum (needs to be not negated. why? because ST! 
             * that's why. */
            tx[6] = (byte)~ComputeChecksum(tx, 2, 4);

            /* try to send command and wait for response */
            try {
                /* send bytes */
                await SerialWrite(tx, 0, 2);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Command Rejected");

                /* send address */
                await SerialWrite(tx, 2, 5);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Address Rejected");
                /* oops, something baaad happened! */
            } catch (Exception) {
                /* release semaphore */
                sem.Release();
                /* re-throw */
                throw;
            }

            /* release semaphore */
            sem.Release();
        }

        /* write memory */
        private async Task Write(uint address, byte[] data, int offset, int length)
        {
            /* command word */
            var tx = new byte[9];
            /* temporary storage for response bytes */
            var tmp = new byte[1];

            /* command code */
            tx[0] = (byte)STCmds.WRITE;
            /* checksum */
            tx[1] = ComputeChecksum(tx, 0, 1);

            /* store address */
            tx[2] = (byte)((address >> 24) & 0xff);
            tx[3] = (byte)((address >> 16) & 0xff);
            tx[4] = (byte)((address >> 8) & 0xff);
            tx[5] = (byte)(address & 0xff);
            /* address checksum (needs to be not negated. why? because ST! 
             * that's why. */
            tx[6] = (byte)~ComputeChecksum(tx, 2, 4);

            /* number of bytes */
            tx[7] = (byte)(length - 1);
            /* data checksum */
            tx[8] = (byte)(~(ComputeChecksum(data, offset, length) ^ tx[7]));

            /* try to send command and wait for response */
            try {
                /* send bytes */
                await SerialWrite(tx, 0, 2);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Command Rejected");

                /* send address */
                await SerialWrite(tx, 2, 5);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Address Rejected");

                /* send the number of bytes */
                await SerialWrite(tx, 7, 1);
                /* send data */
                await SerialWrite(data, offset, length);
                /* send checksum */
                await SerialWrite(tx, 8, 1);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Data Rejected");

                /* oops, something baaad happened! */
            } catch (Exception) {
                /* release semaphore */
                sem.Release();
                /* re-throw */
                throw;
            }

            /* release semaphore */
            sem.Release();
        }

        /* erase memory page */
        private async Task Erase(uint pageNumber)
        {
            /* command word */
            var tx = new byte[5];
            /* temporary storage for response bytes */
            var tmp = new byte[1];

            /* command code */
            tx[0] = (byte)STCmds.ERASE;
            /* checksum */
            tx[1] = ComputeChecksum(tx, 0, 1);

            /* erase single page */
            tx[2] = 0;
            /* set page number */
            tx[3] = (byte)pageNumber;
            /* checksum */
            tx[4] = (byte)~ComputeChecksum(tx, 2, 2);

            /* try to send command and wait for response */
            try {
                /* send bytes */
                await SerialWrite(tx, 0, 2);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Command Rejected");

                /* send address */
                await SerialWrite(tx, 2, 3);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Page Rejected");

                /* oops, something baaad happened! */
            } catch (Exception) {
                /* release semaphore */
                sem.Release();
                /* re-throw */
                throw;
            }

            /* release semaphore */
            sem.Release();
        }

        /* erase memory page */
        private async Task EraseSpecial(STEraseMode mode)
        {
            /* command word */
            var tx = new byte[4];
            /* temporary storage for response bytes */
            var tmp = new byte[1];

            /* command code */
            tx[0] = (byte)STCmds.ERASE;
            /* checksum */
            tx[1] = ComputeChecksum(tx, 0, 1);

            /* erase single page */
            tx[2] = (byte)((int)mode);
            /* checksum */
            tx[3] = (byte)~ComputeChecksum(tx, 2, 2);

            /* try to send command and wait for response */
            try {
                /* send bytes */
                await SerialWrite(tx, 0, 2);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Command Rejected");

                /* send address */
                await SerialWrite(tx, 2, 2);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Special Code Rejected");

                /* oops, something baaad happened! */
            } catch (Exception) {
                /* release semaphore */
                sem.Release();
                /* re-throw */
                throw;
            }

            /* release semaphore */
            sem.Release();
        }

        /* extended erase memory page */
        private async Task ExtendedErase(uint pageNumber)
        {
            /* command word */
            var tx = new byte[7];
            /* temporary storage for response bytes */
            var tmp = new byte[1];

            /* command code */
            tx[0] = (byte)STCmds.EXT_ERASE;
            /* checksum */
            tx[1] = ComputeChecksum(tx, 0, 1);

            /* erase single page */
            tx[2] = 0;
            tx[3] = 0;
            /* set page number */
            tx[4] = (byte)(pageNumber >> 8);
            tx[5] = (byte)(pageNumber >> 0);
            /* checksum */
            tx[6] = (byte)~ComputeChecksum(tx, 2, 5);

            /* try to send command and wait for response */
            try {
                /* send bytes */
                await SerialWrite(tx, 0, 2);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Command Rejected");

                /* send address */
                await SerialWrite(tx, 2, 5);
                /* wait for response code. use longer timeout, erase might
                 * take a while or two. */
                await SerialRead(tmp, 0, 1, 3000);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK) 
                    throw new STBootException("Page Rejected");

            /* oops, something baaad happened! */
            } catch (Exception) {
                /* release semaphore */
                sem.Release();
                /* re-throw */
                throw;
            }

            /* release semaphore */
            sem.Release();
        }

        /* extended erase memory page */
        private async Task ExtendedEraseSpecial(STExtendedEraseMode mode)
        {
            /* command word */
            var tx = new byte[5];
            /* temporary storage for response bytes */
            var tmp = new byte[1];

            /* command code */
            tx[0] = (byte)STCmds.EXT_ERASE;
            /* checksum */
            tx[1] = ComputeChecksum(tx, 0, 1);

            /* erase single page */
            tx[2] = (byte)((int)mode >> 8);
            tx[3] = (byte)((int)mode >> 0);
            /* checksum */
            tx[4] = (byte)~ComputeChecksum(tx, 2, 3);

            /* try to send command and wait for response */
            try {
                /* send bytes */
                await SerialWrite(tx, 0, 2);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Command Rejected");

                /* send address */
                await SerialWrite(tx, 2, 3);
                /* wait for response code. use longer timeout, erase might
                 * take a while or two. */
                await SerialRead(tmp, 0, 1, 10000);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Special code Rejected");

                /* oops, something baaad happened! */
            } catch (Exception) {
                /* release semaphore */
                sem.Release();
                /* re-throw */
                throw;
            }

            /* release semaphore */
            sem.Release();
        }

        /* unprotect flash before writing */
        private async Task WriteUnprotect()
        {
            /* command word */
            var tx = new byte[2];
            /* temporary storage for response bytes */
            var tmp = new byte[1];

            /* command code */
            tx[0] = (byte)STCmds.WR_UNPROTECT;
            /* checksum */
            tx[1] = ComputeChecksum(tx, 0, 1);

            /* try to send command and wait for response */
            try {
                /* send bytes */
                await SerialWrite(tx, 0, 2);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Command Rejected");

                /* wait for response code. use longer timeout, erase might
                 * take a while or two. */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Write Unprotect Rejected");

                /* oops, something baaad happened! */
            } finally {
                /* release semaphore */
                sem.Release();
            }
        }

        /* unprotect flash before reading */
        private async Task ReadUnprotect()
        {
            /* command word */
            var tx = new byte[2];
            /* temporary storage for response bytes */
            var tmp = new byte[1];

            /* command code */
            tx[0] = (byte)STCmds.RD_UNPROTECT;
            /* checksum */
            tx[1] = ComputeChecksum(tx, 0, 1);

            /* try to send command and wait for response */
            try {
                /* send bytes */
                await SerialWrite(tx, 0, 2);
                /* wait for response code */
                await SerialRead(tmp, 0, 1);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Command Rejected");

                /* wait for response code. use longer timeout, erase might
                 * take a while or two. */
                await SerialRead(tmp, 0, 10000);
                /* check response code */
                if (tmp[0] != (byte)STResps.ACK)
                    throw new STBootException("Write Unprotect Rejected");

                /* oops, something baaad happened! */
            } finally {
                /* release semaphore */
                sem.Release();
            }
        }

        /* compute checksum */
        private byte ComputeChecksum(byte[] data, int offset, int count)
        {
            /* initial value */
            byte xor = 0xff;
            /* compute */
            for (int i = offset; i < count + offset; i++)
                xor ^= data[i];

            /* return value */
            return xor;
        }

        /* write to serial port */
        private async Task SerialWrite(byte[] data, int offset, int count)
        {
            /* shorter name */
            var bs = sp.BaseStream;

            /* write operation */
            await bs.WriteAsync(data, offset, count);
        }

        /* standard read with timeout equal to 1s */
        private async Task SerialRead(byte[] data, int offset, int count)
        {
            await SerialRead(data, offset, count, 1000);
        }

        /* read 'length' number of bytes from serial port */
        private async Task SerialRead(byte[] data, int offset, int count, int timeout)
        {
            /* shorter name */
            var bs = sp.BaseStream;
            /* number of bytes read */
            int br = 0;

            /* read until all bytes are fetched from serial port */
            while (br < count) {
                /* this try is for timeout handling */
                try {
                    /* prepare task */
                    br += await bs.ReadAsync(data, offset + br, count - br).
                        WithTimeout(timeout);
                /* got handling? */
                } catch (OperationCanceledException) {
                    /* rethrow */
                    throw new STBootException("Timeout");
                }
            }
        }
    }
}
