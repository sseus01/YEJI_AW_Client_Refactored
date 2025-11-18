namespace YEJI_AW_Client
{
    partial class IdleReasonForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(IdleReasonForm));
            labelInstruction = new Label();
            comboBoxReason = new ComboBox();
            textBoxDetail = new TextBox();
            buttonSave = new Button();
            labelIdleTime = new Label();
            label1 = new Label();
            SuspendLayout();
            // 
            // labelInstruction
            // 
            labelInstruction.AutoSize = true;
            labelInstruction.Location = new Point(51, 67);
            labelInstruction.Name = "labelInstruction";
            labelInstruction.Size = new Size(123, 15);
            labelInstruction.TabIndex = 0;
            labelInstruction.Text = "자리비움 사유를 선택";
            // 
            // comboBoxReason
            // 
            comboBoxReason.FormattingEnabled = true;
            comboBoxReason.Location = new Point(195, 64);
            comboBoxReason.Name = "comboBoxReason";
            comboBoxReason.Size = new Size(121, 23);
            comboBoxReason.TabIndex = 1;
            // 
            // textBoxDetail
            // 
            textBoxDetail.Location = new Point(48, 96);
            textBoxDetail.Multiline = true;
            textBoxDetail.Name = "textBoxDetail";
            textBoxDetail.Size = new Size(279, 76);
            textBoxDetail.TabIndex = 2;
            // 
            // buttonSave
            // 
            buttonSave.Location = new Point(141, 189);
            buttonSave.Name = "buttonSave";
            buttonSave.Size = new Size(75, 23);
            buttonSave.TabIndex = 3;
            buttonSave.Text = "저장";
            buttonSave.UseVisualStyleBackColor = true;
            // 
            // labelIdleTime
            // 
            labelIdleTime.AutoSize = true;
            labelIdleTime.Location = new Point(141, 33);
            labelIdleTime.Name = "labelIdleTime";
            labelIdleTime.Size = new Size(143, 15);
            labelIdleTime.TabIndex = 4;
            labelIdleTime.Text = "자리비움 시작~종료 시간";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(51, 33);
            label1.Name = "label1";
            label1.Size = new Size(94, 15);
            label1.TabIndex = 5;
            label1.Text = "자리비움 시간 : ";
            // 
            // IdleReasonForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(370, 230);
            Controls.Add(label1);
            Controls.Add(labelIdleTime);
            Controls.Add(buttonSave);
            Controls.Add(textBoxDetail);
            Controls.Add(comboBoxReason);
            Controls.Add(labelInstruction);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "IdleReasonForm";
            Text = "자리비움 사유 입력";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelInstruction;
        private ComboBox comboBoxReason;
        private TextBox textBoxDetail;
        private Button buttonSave;
        private Label labelIdleTime;
        private Label label1;
    }
}