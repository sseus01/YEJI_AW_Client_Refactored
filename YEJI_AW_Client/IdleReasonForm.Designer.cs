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
            comboBoxLevel1 = new ComboBox();
            comboBoxLevel2 = new ComboBox();
            labelLevel3Value = new Label();
            textBoxDetail = new TextBox();
            buttonSave = new RoundButton();
            buttonShowAll = new RoundButton();
            labelIdleTime = new Label();
            label1 = new Label();
            labelLevel1 = new Label();
            labelLevel2 = new Label();
            labelLevel3 = new Label();
            labelDetailInstruction = new Label();
            SuspendLayout();
            // 
            // labelInstruction
            // 
            labelInstruction.AutoSize = true;
            labelInstruction.Location = new Point(29, 63);
            labelInstruction.Name = "labelInstruction";
            labelInstruction.Size = new Size(159, 15);
            labelInstruction.TabIndex = 0;
            labelInstruction.Text = "자리비움 사유를 선택하세요";
            // 
            // comboBoxLevel1
            // 
            comboBoxLevel1.FormattingEnabled = true;
            comboBoxLevel1.Location = new Point(29, 107);
            comboBoxLevel1.Name = "comboBoxLevel1";
            comboBoxLevel1.Size = new Size(110, 23);
            comboBoxLevel1.TabIndex = 1;
            // 
            // comboBoxLevel2
            // 
            comboBoxLevel2.FormattingEnabled = true;
            comboBoxLevel2.Location = new Point(145, 107);
            comboBoxLevel2.Name = "comboBoxLevel2";
            comboBoxLevel2.Size = new Size(110, 23);
            comboBoxLevel2.TabIndex = 2;
            // 
            // labelLevel3Value
            // 
            labelLevel3Value.Location = new Point(267, 110);
            labelLevel3Value.Name = "labelLevel3Value";
            labelLevel3Value.Size = new Size(110, 15);
            labelLevel3Value.TabIndex = 3;
            // 
            // textBoxDetail
            // 
            textBoxDetail.Location = new Point(29, 155);
            textBoxDetail.Multiline = true;
            textBoxDetail.Name = "textBoxDetail";
            textBoxDetail.Size = new Size(480, 76);
            textBoxDetail.TabIndex = 4;
            // 
            // buttonSave
            // 
            buttonSave.Location = new Point(231, 244);
            buttonSave.Name = "buttonSave";
            buttonSave.Size = new Size(75, 23);
            buttonSave.TabIndex = 5;
            buttonSave.Text = "저장";
            buttonSave.UseVisualStyleBackColor = true;
            // 
            // buttonShowAll
            // 
            buttonShowAll.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonShowAll.Location = new Point(419, 77);
            buttonShowAll.Name = "buttonShowAll";
            buttonShowAll.Size = new Size(90, 27);
            buttonShowAll.TabIndex = 3;
            buttonShowAll.Text = "전체보기(+)";
            buttonShowAll.UseVisualStyleBackColor = true;
            // 
            // labelIdleTime
            // 
            labelIdleTime.AutoSize = true;
            labelIdleTime.Location = new Point(145, 30);
            labelIdleTime.Name = "labelIdleTime";
            labelIdleTime.Size = new Size(143, 15);
            labelIdleTime.TabIndex = 4;
            labelIdleTime.Text = "자리비움 시작~종료 시간";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(29, 30);
            label1.Name = "label1";
            label1.Size = new Size(94, 15);
            label1.TabIndex = 5;
            label1.Text = "자리비움 시간 : ";
            // 
            // labelLevel1
            // 
            labelLevel1.AutoSize = true;
            labelLevel1.Location = new Point(29, 89);
            labelLevel1.Name = "labelLevel1";
            labelLevel1.Size = new Size(31, 15);
            labelLevel1.TabIndex = 6;
            labelLevel1.Text = "구분";
            // 
            // labelLevel2
            // 
            labelLevel2.AutoSize = true;
            labelLevel2.Location = new Point(145, 89);
            labelLevel2.Name = "labelLevel2";
            labelLevel2.Size = new Size(55, 15);
            labelLevel2.TabIndex = 7;
            labelLevel2.Text = "세부유형";
            // 
            // labelLevel3
            // 
            labelLevel3.AutoSize = true;
            labelLevel3.Location = new Point(261, 89);
            labelLevel3.Name = "labelLevel3";
            labelLevel3.Size = new Size(23, 15);
            labelLevel3.TabIndex = 8;
            labelLevel3.Text = "예)";
            // 
            // labelDetailInstruction
            // 
            labelDetailInstruction.AutoSize = true;
            labelDetailInstruction.Location = new Point(29, 137);
            labelDetailInstruction.Name = "labelDetailInstruction";
            labelDetailInstruction.Size = new Size(227, 15);
            labelDetailInstruction.TabIndex = 9;
            labelDetailInstruction.Text = "아래의 메모창에 상세 사유를 입력하세요";
            // 
            // IdleReasonForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(540, 290);
            Controls.Add(buttonShowAll);
            Controls.Add(labelDetailInstruction);
            Controls.Add(labelLevel3Value);
            Controls.Add(labelLevel2);
            Controls.Add(labelLevel1);
            Controls.Add(label1);
            Controls.Add(labelIdleTime);
            Controls.Add(buttonSave);
            Controls.Add(textBoxDetail);
            Controls.Add(labelLevel3);
            Controls.Add(comboBoxLevel2);
            Controls.Add(comboBoxLevel1);
            Controls.Add(labelInstruction);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "IdleReasonForm";
            Text = "자리비움 사유 입력";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelInstruction;
        private ComboBox comboBoxLevel1;
        private ComboBox comboBoxLevel2;
        private TextBox textBoxDetail;
        private Button buttonSave;
        private Label labelIdleTime;
        private Label label1;
        private Label labelLevel1;
        private Label labelLevel2;
        private Label labelLevel3;
        private Label labelDetailInstruction;
        private Label labelLevel3Value;
        private Button buttonShowAll;
    }
}