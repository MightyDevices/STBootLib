using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO.Ports;
using System.IO;
using System.Threading;
using System.Media;
using STBootLib;

namespace STUploader
{
    public partial class fMainForm : Form
    {
        /* current file name */
        string fileName;
        /* com port name */
        string portName;
        /* baudrate */
        uint baudRate;
        /* address */
        uint address;
        /* page */
        uint page;

        /* default address */
        const uint baseAddress = 0x08000000;

        /* constructor */
        public fMainForm()
        {
            /* initialize all components */
            InitializeComponent();

            /* set default baurate selection */
            cbBauds.SelectedIndex = 6;
            cbPSize.SelectedIndex = 0;
            tbAddress.SelectedIndex = 0;
            /* set defaul address */
            address = baseAddress;
        }

        /* drop down list opened */
        private void cbPorts_DropDown(object sender, EventArgs e)
        {
            /* apply to combo box */
            cbPorts.DataSource = SerialPort.GetPortNames();
        }

        /* port selection was altered */
        private void cbPorts_SelectedIndexChanged(object sender, EventArgs e)
        {
            /* valid selection made? */
            if (cbPorts.SelectedIndex == -1) {
                /* nope, disable button */
                bWrite.Enabled = bJump.Enabled = false;
            } else {
                /* store com port name */
                portName = (string)cbPorts.SelectedItem;
                /* enable button */
                bWrite.Enabled = bJump.Enabled = true;
            }
        }

        /* open file clicked */
        private void bOpenFile_Click(object sender, EventArgs e)
        {
            /* file selected? */
            if (ofdOpen.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                /* set file name */
                tbFileName.Text = ofdOpen.SafeFileName;
                /* set tool tip */
                ttToolTip.SetToolTip(tbFileName, ofdOpen.FileName);
                /* store full path */
                fileName = ofdOpen.FileName;
            }
        }

        /* jump button pressed */
        private async void bJump_Click(object sender, EventArgs e)
        {
            /* disable button */
            bJump.Enabled = bWrite.Enabled = false;
            /* get port name */
            string pName = (string)cbPorts.SelectedItem;
            /* get baud rate */
            uint bauds = uint.Parse((string)cbBauds.SelectedItem);
            /* get address */
            uint address = Convert.ToUInt32(tbAddress.Text, 16);

            try {
                /* try to upload */
                await Jump(address);
            } catch (Exception ex) {
                /* set message */
                UpdateStatus(true, ex.Message);
            } finally {
                bJump.Enabled = bWrite.Enabled = true;
            }
        }

        /* write clicked */
        private async void bWrite_Click(object sender, EventArgs e)
        {
            /* disable button */
            bJump.Enabled = bWrite.Enabled = false;
            /* get port name */
            string pName = (string)cbPorts.SelectedItem;
            /* get baud rate */
            uint bauds = uint.Parse((string)cbBauds.SelectedItem);
            /* get address */
            uint address = Convert.ToUInt32(tbAddress.Text, 16);

            try {
                /* read file */
                var bin = await ReadFile(fileName);
                /* try to upload */
                await UploadFile(pName, bauds, bin, address, address);
            } catch (Exception ex) {
                /* set message */
                UpdateStatus(true, ex.Message);
            } finally {
                bJump.Enabled = bWrite.Enabled = true;
            }
        }



        /* baud rate changed */
        private void cbBauds_SelectedIndexChanged(object sender, EventArgs e)
        {
            /* convert from string */
            baudRate = uint.Parse((string)cbBauds.SelectedItem);
        }

        /* parse address */
        private void tbAddress_Leave(object sender, EventArgs e)
        {
            /* parsed address */
            uint addr;

            /* try to parse input */
            try {
                /* convert address field */
                addr = Convert.ToUInt32(tbAddress.Text, 16);
            /* malformed address value */
            } catch (OverflowException) {
                /* set message */
                tsslStatus.Text = "Address too large!";
                /* restore default value */
                addr = baseAddress;
            /* all other errors go here */
            } catch (Exception) {
                /* set message */
                tsslStatus.Text = "Incorrect hex value";
                /* restore default value */
                addr = baseAddress;
            }

            /* store realigned address */
            address = addr & 0xffffff00;
            /* start page - end page */
            page = (address - baseAddress) / 256;

            /* rewrite */
            tbAddress.Text = string.Format("0x{0:X8}", address);
        }

        /* load firmware file */
        private async Task<byte[]> ReadFile(string fname)
        {
            byte[] bin;

            /* open file */
            using (var s = new FileStream(fname, FileMode.Open,
                    FileAccess.Read)) {
                /* allocate memory */
                bin = new byte[s.Length];
                /* read file contents */
                await s.ReadAsync(bin, 0, bin.Length);
            }

            /* return binary image */
            return bin;
        }

        /* upload a binary image to uC */
        private async Task UploadFile(string portName, uint baudRate,
            byte[] bin, uint address, uint jumpAddress)
        {
            /* get page size */
            uint psize = uint.Parse(cbPSize.SelectedItem as string);

            /* create new programming interface object */
            using (var uc = new STBoot()) {
                /* open device */
                uc.Open(portName, baudRate);
                /* initialize communication */
                await uc.Initialize();
                /* update the status */
                UpdateStatus(false, string.Format("Connected: Ver: {0}, PID: 0x{1:X4}",
                    uc.Version, uc.ProductID));
                /* give some chance see the message */
                await Task.Delay(500);

                /* apply new message */
                UpdateStatus(false, "Erasing...");

                /* checked? */
                if (cbxErase.Checked) {
                    await uc.GlobalErase();
                } else {
                    /* erase operation */
                    for (uint i = 0; i < bin.Length; i += psize) {
                        /* erase page */
                        await uc.ErasePage((i + address - 0x08000000) / psize);
                        /* update progress bar */
                        UpdateProgress((int)i * 100 / bin.Length);
                    }
                }

                /* apply new message */
                UpdateStatus(false, "Programming...");
                /* progress reporter */
                var p = new Progress<STBootProgress>(UpdateProgress);
                /* write memory */
                await uc.WriteMemory(address, bin, 0, bin.Length, p,
                    CancellationToken.None);
                /* update the status */
                UpdateStatus(false, string.Format("Success: {0} bytes written",
                    bin.Length));

                /* go! */
                await uc.Jump(jumpAddress);

                /* end communication */
                uc.Close();
            }
        }

        /* execute code */
        private async Task Jump(uint address)
        {
            /* create new programming interface object */
            using (var uc = new STBoot()) {
                /* open device */
                uc.Open(portName, baudRate);
                /* initialize communication */
                await uc.Initialize();
                /* go! */
                await uc.Jump(address);
                /* end communication */
                uc.Close();
            }
        }

        /* set current progress */
        private void UpdateProgress(STBootProgress p)
        {
            /* converts bytes to percentage */
            UpdateProgress(100 * p.bytesProcessed / p.bytesTotal);
        }

        /* set current progress */
        private void UpdateProgress(int percent)
        {
            /* set progress bar value */
            pbProgress.Value = percent;
            /* set label */
            lProgress.Text = percent.ToString() + "%";
        }

        /* update status bar */
        private void UpdateStatus(bool ding, string text)
        {
            /* text */
            tsslStatus.Text = text;
            /* play a system sound? */
            if (ding) {
                /* ^^ ding! */
                SystemSounds.Exclamation.Play();
            }
        }

        private void cbxErase_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void cbPSize_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void tbAddress_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
