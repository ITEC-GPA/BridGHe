using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Grasshopper;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Forms
{
    public partial class LoadCombinationsForm : Form
    {
        protected LoadCaseModel[] _loadCases;
        protected List<GH_LoadCombination> _values;
        // R65-C2-05: the case records and whether the combinations are made by their GPC datum alone (LoadCombinationRows).
        private readonly IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> _caseRecords;
        private readonly bool _gpcOnly;

        public List<GH_LoadCombination> Values => _values;

        /// <summary>
        /// Public contructor
        /// </summary>
        /// <param name="loadCases">The load cases names array to use for creating the columns of the DataGrid</param>
        /// <param name="values">The previous values to restore as rows of the DataGrid</param>
        /// <param name="caseRecords">R65-C2-05: the case records of the parameter (the columns when the combinations are given by their GPC datum alone).</param>
        public LoadCombinationsForm(LoadCaseModel[] loadCases, List<GH_LoadCombination> values = null, IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> caseRecords = null)
        {
            Owner = Instances.DocumentEditor;
            InitializeComponent();
            _loadCases = loadCases;
            _caseRecords = caseRecords;
            _gpcOnly = LoadCombinationRows.GpcOnly(values, loadCases, caseRecords);
            string[] columns = LoadCombinationRows.Columns(_gpcOnly, loadCases, caseRecords);

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
            // R65-C2-05: the combinations of the rows (LoadCombinationRows: the same checks and messages; by their GPC datum alone when the
            // inputs are).
            var rows = new List<(string, IReadOnlyList<object>)>();
            for (int i = 0; i < dataGridView.RowCount - 1; i++)
                rows.Add((dataGridView.Rows[i].Cells[0].Value.ToString(), Enumerable.Range(1, dataGridView.ColumnCount - 1).Select(j => dataGridView.Rows[i].Cells[j].Value).ToArray()));
            var columns = Enumerable.Range(1, dataGridView.ColumnCount - 1).Select(j => dataGridView.Columns[j].Name).ToArray();
            var (values, error) = LoadCombinationRows.Combinations(rows, columns, _loadCases, _caseRecords, _gpcOnly);
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
