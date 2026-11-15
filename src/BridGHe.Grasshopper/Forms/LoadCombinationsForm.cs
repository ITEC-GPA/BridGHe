using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Grasshopper;
using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Forms
{
    public partial class LoadCombinationsForm : Form
    {
        protected List<GH_LoadCombination> _values;
        // R65-C2-05: the case records (LoadCombinationRows). P2K (P1): the combinations are made by their GPC datum alone.
        private readonly IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> _caseRecords;

        public List<GH_LoadCombination> Values => _values;

        /// <summary>
        /// Public contructor
        /// </summary>
        /// <param name="values">The previous values to restore as rows of the DataGrid</param>
        /// <param name="caseRecords">R65-C2-05: the case records of the parameter (the columns of the DataGrid). P2K (P1): the FEM cases
        /// parameter removed.</param>
        public LoadCombinationsForm(List<GH_LoadCombination> values = null, IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> caseRecords = null)
        {
            Owner = Instances.DocumentEditor;
            InitializeComponent();
            _caseRecords = caseRecords;
            string[] columns = LoadCombinationRows.CaseColumns(caseRecords);

            dataGridView.ColumnCount = columns.Length + 1;
            dataGridView.Columns[0].Name = "Combo Name";
            dataGridView.Columns[0].ValueType = typeof(string);
            for (int i = 0; i < columns.Length; i++)
            {
                dataGridView.Columns[i + 1].Name = columns[i];
                dataGridView.Columns[i + 1].ValueType = typeof(double);
            }

            // Fill the current values (R65-C2-05: a combination given by its GPC datum alone from its datum).
            if (values != null)
            {
                var rows = LoadCombinationRows.Of(values);
                dataGridView.RowCount = rows.Count + 1;
                for (int i = 0; i < rows.Count; i++)
                {
                    dataGridView.Rows[i].Cells[0].Value = rows[i].Name;
                    foreach (var item in rows[i].Factors)
                        if (dataGridView.Columns.Contains(item.Key)) dataGridView.Rows[i].Cells[item.Key].Value = item.Value;
                }
            }
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            // R65-C2-05: the combinations of the rows (LoadCombinationRows: the same checks and messages; by their GPC datum alone).
            var rows = new List<(string, IReadOnlyList<object>)>();
            for (int i = 0; i < dataGridView.RowCount - 1; i++)
                rows.Add((dataGridView.Rows[i].Cells[0].Value.ToString(), Enumerable.Range(1, dataGridView.ColumnCount - 1).Select(j => dataGridView.Rows[i].Cells[j].Value).ToArray()));
            var columns = Enumerable.Range(1, dataGridView.ColumnCount - 1).Select(j => dataGridView.Columns[j].Name).ToArray();
            var (values, error) = LoadCombinationRows.Make(rows, columns, _caseRecords);
            if (error != null)
            {
                MessageBox.Show(error, "Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            _values = values;
            DialogResult = DialogResult.OK;
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private void dataGridView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.V)
            {
                string s = Clipboard.GetText();
                string[] lines = s.Split('\n');
                int row = dataGridView.CurrentCell.RowIndex;
                int col = dataGridView.CurrentCell.ColumnIndex;
                if (row + lines.Length + 1 > dataGridView.RowCount)
                {
                    dataGridView.RowCount = row + lines.Length + 1;
                }
                foreach (string line in lines)
                {
                    if (row < dataGridView.RowCount && line.Length > 0)
                    {
                        string[] cells = line.Split('\t');
                        for (int i = 0; i < cells.GetLength(0); ++i)
                        {
                            if (col + i < this.dataGridView.ColumnCount)
                            {
                                try
                                {
                                    dataGridView[col + i, row].Value = Convert.ChangeType(cells[i], dataGridView[col + i, row].ValueType);
                                }
                                catch
                                {
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                        row++;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
    }
}
