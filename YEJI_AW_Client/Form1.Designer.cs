namespace YEJI_AW_Client
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                {
                    components.Dispose();
                }

                // Dispose all timers to prevent resource leaks
                popupTimer?.Dispose();
                pcOffTimer?.Dispose();
                shutdownCountdownTimer?.Dispose();
                configTimer?.Dispose();
                memoryTrimTimer?.Dispose();
                heartbeatTimer?.Dispose();
                updateCheckTimer?.Dispose();
                employeeOvertimeStatusTimer?.Dispose();
                managerNotificationTimer?.Dispose();
                tempDisableTrayTimer?.Dispose();

                // Dispose semaphores
                heartbeatSemaphore?.Dispose();
                idleIntervalSemaphore?.Dispose();

                // Dispose forms
                pcOffAlertForm?.Dispose();
                managerNotificationListForm?.Dispose();
                tempDisableTrayForm?.Dispose();

                // Dispose tray menu
                trayMenu?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            notifyIcon = new NotifyIcon(components);
            idleTimer = new System.Windows.Forms.Timer(components);
            flowLayoutPanel1 = new FlowLayoutPanel();
            SuspendLayout();
            // 
            // notifyIcon
            // 
            notifyIcon.Icon = (Icon)resources.GetObject("notifyIcon.Icon");
            notifyIcon.Text = "YEJI_AW_Client";
            notifyIcon.Visible = true;
            // 
            // flowLayoutPanel1
            // 
            flowLayoutPanel1.Location = new Point(103, 132);
            flowLayoutPanel1.Name = "flowLayoutPanel1";
            flowLayoutPanel1.Size = new Size(200, 100);
            flowLayoutPanel1.TabIndex = 0;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(flowLayoutPanel1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            Text = "Form1";
            ResumeLayout(false);
        }

        #endregion

        private NotifyIcon notifyIcon;
        private System.Windows.Forms.Timer idleTimer;
        private FlowLayoutPanel flowLayoutPanel1;
    }
}