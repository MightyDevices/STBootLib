using System;
using System.IO;
using System.IO.Ports;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using STBootLib;

namespace STUploader
{
    /// <summary>
    /// Main form.
    /// </summary>
    public partial class MainForm : Form
    {
        /// <summary> default address. </summary>
        private const uint BaseAddress = 0x08000000;

        private string fileName;
        private string portName;
        private uint baudRate;
        private uint address;
        private uint page;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainForm"/> class.
        /// </summary>
        public MainForm()
        {
            this.InitializeComponent();

            // set default baud rate selection
            this.cbBauds.SelectedIndex = 6;
            this.cbPSize.SelectedIndex = 0;

            // set default address
            this.address = BaseAddress;
        }

        private void cbPorts_DropDown(object sender, EventArgs e)
        {
            this.cbPorts.DataSource = SerialPort.GetPortNames();
        }

        /// <summary> Port selection was altered. </summary>
        private void cbPorts_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.cbPorts.SelectedIndex == -1)
            {
                this.bWrite.Enabled = this.bJump.Enabled = false;
            }
            else
            {
                this.portName = (string)this.cbPorts.SelectedItem;
                this.bWrite.Enabled = this.bJump.Enabled = true;
            }
        }

        private void bOpenFile_Click(object sender, EventArgs e)
        {
            if (this.ofdOpen.ShowDialog() == DialogResult.OK)
            {
                this.tbFileName.Text = this.ofdOpen.SafeFileName;
                this.ttToolTip.SetToolTip(this.tbFileName, this.ofdOpen.FileName);
                this.fileName = this.ofdOpen.FileName;
            }
        }

        private async void bJump_Click(object sender, EventArgs e)
        {
            this.bJump.Enabled = this.bWrite.Enabled = false;
            var address = Convert.ToUInt32(this.tbAddress.Text, 16);

            try
            {
                await this.Jump(address);
            }
            catch (Exception ex)
            {
                this.UpdateStatus(true, ex.Message);
            }
            finally
            {
                this.bJump.Enabled = this.bWrite.Enabled = true;
            }
        }

        private async void bWrite_Click(object sender, EventArgs e)
        {
            this.bJump.Enabled = this.bWrite.Enabled = false;
            var pName = (string)this.cbPorts.SelectedItem;
            var bauds = uint.Parse((string)this.cbBauds.SelectedItem);
            var address = Convert.ToUInt32(this.tbAddress.Text, 16);

            try
            {
                var bin = await this.ReadFile(this.fileName);
                await this.UploadFile(pName, bauds, bin, address, address);
            }
            catch (Exception ex)
            {
                this.UpdateStatus(true, ex.Message);
            }
            finally
            {
                this.bJump.Enabled = this.bWrite.Enabled = true;
            }
        }

        /// <summary> Baud rate changed. </summary>
        private void cbBauds_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.baudRate = uint.Parse((string)this.cbBauds.SelectedItem);
        }

        /// <summary> Parse address. </summary>
        private void tbAddress_Leave(object sender, EventArgs e)
        {
            uint address;

            try
            {
                address = Convert.ToUInt32(this.tbAddress.Text, 16);
            }
            catch (OverflowException)
            {
                this.tsslStatus.Text = "Address too large!";
                address = BaseAddress;
            }
            catch (Exception)
            {
                this.tsslStatus.Text = "Incorrect hex value";
                address = BaseAddress;
            }

            this.address = address & 0xffffff00;
            this.page = (this.address - BaseAddress) / 256;

            this.tbAddress.Text = string.Format("0x{0:X8}", this.address);
        }

        /// <summary> Load firmware file. </summary>
        private async Task<byte[]> ReadFile(string fname)
        {
            byte[] bin;

            using (var s = new FileStream(fname, FileMode.Open, FileAccess.Read))
            {
                bin = new byte[s.Length];
                await s.ReadAsync(bin, 0, bin.Length);
            }

            return bin;
        }

        private async Task UploadFile(
            string portName,
            uint baudRate,
            byte[] bin,
            uint address,
            uint jumpAddress)
        {
            uint pageSize = uint.Parse(this.cbPSize.SelectedItem as string);

            using (var uc = new STBoot())
            {
                uc.Open(portName, baudRate);
                await uc.Initialize();
                this.UpdateStatus(false, $"Connected: Ver: {uc.Version}, PID: 0x{uc.ProductId:X4}");
                await Task.Delay(500);

                // apply new message
                this.UpdateStatus(false, "Erasing...");

                if (this.cbxErase.Checked)
                {
                    await uc.GlobalErase();
                }
                else
                {
                    for (uint i = 0; i < bin.Length; i += pageSize)
                    {
                        await uc.ErasePage((i + address - 0x08000000) / pageSize);
                        this.UpdateProgress((int)i * 100 / bin.Length);
                    }
                }

                // apply new message
                this.UpdateStatus(false, "Programming...");

                // progress reporter
                var p = new Progress<STBootProgress>(this.UpdateProgress);
                await uc.WriteMemory(address, bin, 0, bin.Length, p, CancellationToken.None);
                this.UpdateStatus(false, $"Success: {bin.Length} bytes written");

                await uc.Jump(jumpAddress);
                uc.Close();
            }
        }

        private async Task Jump(uint address)
        {
            using (var uc = new STBoot())
            {
                uc.Open(this.portName, this.baudRate);
                await uc.Initialize();
                await uc.Jump(address);
                uc.Close();
            }
        }

        /// <summary> Set current progress. </summary>
        private void UpdateProgress(STBootProgress p)
        {
            /* converts bytes to percentage */
            this.UpdateProgress(100 * p.BytesProcessed / p.BytesTotal);
        }

        /// <summary> Set current progress. </summary>
        private void UpdateProgress(int percent)
        {
            this.pbProgress.Value = percent;
            this.lProgress.Text = percent.ToString() + "%";
        }

        private void UpdateStatus(bool ding, string text)
        {
            this.tsslStatus.Text = text;

            if (ding)
            {
                SystemSounds.Exclamation.Play();
            }
        }

        private void cbxErase_CheckedChanged(object sender, EventArgs e)
        {
        }

        private void cbPSize_SelectedIndexChanged(object sender, EventArgs e)
        {
        }
    }
}
