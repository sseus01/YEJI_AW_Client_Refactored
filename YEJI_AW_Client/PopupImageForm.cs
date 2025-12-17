using System;
using System.Drawing;
using System.Windows.Forms;

namespace YEJI_AW_Client
{
    public partial class PopupImageForm : Form
    {
        public PopupImageForm(string imageUrl)
        {
            InitializeComponent();

            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(1051, 792);
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowIcon = false;
            this.Text = string.Empty;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            try
            {
                pictureBox1.Load(imageUrl);
            }
            catch
            {
                pictureBox1.Image = null;
            }

            // 이미지 더블클릭하면 팝업 닫힘
            pictureBox1.DoubleClick += (s, e) => this.Close();

            this.FormClosed += (s, e) =>
            {
                if (pictureBox1.Image != null)
                {
                    pictureBox1.Image.Dispose();
                    pictureBox1.Image = null;
                }

                pictureBox1.Dispose();
                MemoryOptimizer.TrimWorkingSet();
            };
        }
    }
}
