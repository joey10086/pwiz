/*
 * Original author: Tahmina Jahan <tabaker .at. u.washington.edu>,
 *                  UWPR, Department of Genome Sciences, UW
 *
 * Copyright 2012 University of Washington - Seattle, WA
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using pwiz.Skyline.Alerts;
using pwiz.Skyline.Controls;
using pwiz.Skyline.Controls.SeqNode;
using pwiz.Skyline.EditUI;
using pwiz.Skyline.Model;
using pwiz.Skyline.Model.DocSettings;
using pwiz.Skyline.Model.DocSettings.Extensions;
using pwiz.Skyline.Properties;
using pwiz.Skyline.SettingsUI;
using pwiz.Skyline.Util;
using pwiz.Skyline.Util.Extensions;

namespace pwiz.Skyline.FileUI.PeptideSearch
{
    public partial class ImportFastaControl : UserControl
    {
        public ImportFastaControl(SkylineWindow skylineWindow)
        {
            SkylineWindow = skylineWindow;

            InitializeComponent();

            ImportFastaHelper = new ImportFastaHelper(tbxFasta, tbxError, panelError);

            tbxFastaHeightDifference = Height - tbxFasta.Height;

            _driverEnzyme = new SettingsListComboDriver<Enzyme>(comboEnzyme, Settings.Default.EnzymeList);
            _driverEnzyme.LoadList(SkylineWindow.Document.Settings.PeptideSettings.Enzyme.GetKey());

            MaxMissedCleavages = skylineWindow.Document.Settings.PeptideSettings.DigestSettings.MaxMissedCleavages;
        }

        private SkylineWindow SkylineWindow { get; set; }
        private Form WizardForm { get { return FormEx.GetParentForm(this); } }

        private ImportFastaHelper ImportFastaHelper { get; set; }
        private readonly SettingsListComboDriver<Enzyme> _driverEnzyme;

        private const long MAX_FASTA_TEXTBOX_LENGTH = 5 << 20;    // 5 MB
        private bool _fastaFile
        {
            get { return !tbxFasta.Multiline; }
            set
            {
                tbxFasta.Multiline = !value;
                if (tbxFasta.Multiline)
                    tbxFasta.Height = Height - tbxFastaHeightDifference;
            }
        }
        private readonly int tbxFastaHeightDifference;

        public bool ContainsFastaContent { get { return !string.IsNullOrWhiteSpace(tbxFasta.Text); } }

        public Enzyme Enzyme
        {
            get { return Settings.Default.GetEnzymeByName(comboEnzyme.SelectedItem.ToString()); }
            set { comboEnzyme.SelectedItem = value; }
        }

        public int MaxMissedCleavages
        {
            get
            {
                return int.Parse(cbMissedCleavages.SelectedItem.ToString());
            }
            set
            {
                cbMissedCleavages.SelectedItem = value.ToString(CultureInfo.CurrentCulture);
                if (cbMissedCleavages.SelectedIndex < 0)
                    cbMissedCleavages.SelectedIndex = 0;
            }
        }

        private void browseFastaBtn_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog
            {
                Title = Resources.ImportFastaControl_browseFastaBtn_Click_Open_FASTA,
                InitialDirectory = Settings.Default.FastaDirectory,
                CheckPathExists = true
                // FASTA files often have no extension as well as .fasta and others
            })
            {
                if (dlg.ShowDialog(WizardForm) == DialogResult.OK)
                {
                    SetFastaContent(dlg.FileName);
                }
            }

        }

        private void tbxFasta_TextChanged(object sender, EventArgs e)
        {
            ImportFastaHelper.ClearFastaError();
        }

        public void SetFastaContent(string fastaFilePath)
        {
            try
            {
                var fileInfo = new FileInfo(fastaFilePath);
                if (fileInfo.Length > MAX_FASTA_TEXTBOX_LENGTH)
                {
                    _fastaFile = true;
                    tbxFasta.Text = fastaFilePath;
                }
                else
                {
                    _fastaFile = false;
                    tbxFasta.Text = GetFastaFileContent(fastaFilePath);
                }
            }
            catch (Exception x)
            {
                MessageDlg.Show(WizardForm, TextUtil.LineSeparate(string.Format(Resources.ImportFastaControl_SetFastaContent_Error_adding_FASTA_file__0__, fastaFilePath), x.Message));
            }
        }

        private string GetFastaFileContent(string fastaFileName)
        {
            string fastaText = string.Empty;
            try
            {
                using (var readerFasta = new StreamReader(fastaFileName))
                {
                    var sb = new StringBuilder();
                    string line;
                    while ((line = readerFasta.ReadLine()) != null)
                        sb.AppendLine(line);
                    fastaText = sb.ToString();
                }
            }
            catch (Exception x)
            {
                MessageDlg.Show(WizardForm, TextUtil.LineSeparate(string.Format(Resources.ImportFastaControl_GetFastaFileContent_Failed_reading_the_file__0__, fastaFileName), x.Message));
            }

            return fastaText;
        }

        private bool VerifyAtLeastOnePrecursorTransition(SrmDocument doc)
        {
            if (!HasPrecursorTransitions(doc))
            {
                MessageDlg.Show(WizardForm, Resources.ImportFastaControl_VerifyAtLeastOnePrecursorTransition_The_document_must_contain_at_least_one_precursor_transition_in_order_to_proceed_);
                return false;
            }

            return true;
        }

        private static bool HasPrecursorTransitions(SrmDocument doc)
        {
            return doc.Transitions.Any(nodeTran => nodeTran.Transition.IonType == IonType.precursor);
        }

        private static SrmDocument ChangeAutoManageChildren(SrmDocument doc, PickLevel which, bool autoPick)
        {
            var refine = new RefinementSettings { AutoPickChildrenAll = which, AutoPickChildrenOff = !autoPick };
            return refine.Refine(doc);
        }

        public bool ImportFasta()
        {
            var settings = SkylineWindow.Document.Settings;
            var peptideSettings = settings.PeptideSettings;
            int missedCleavages = MaxMissedCleavages;
            var enzyme = Enzyme;
            if (!Equals(missedCleavages, peptideSettings.DigestSettings.MaxMissedCleavages) || !Equals(enzyme, peptideSettings.Enzyme))
            {
                var digest = new DigestSettings(missedCleavages, peptideSettings.DigestSettings.ExcludeRaggedEnds);
                peptideSettings = peptideSettings.ChangeDigestSettings(digest).ChangeEnzyme(enzyme);
                SkylineWindow.ModifyDocument(string.Format(Resources.ImportFastaControl_ImportFasta_Change_digestion_settings), doc =>
                    doc.ChangeSettings(settings.ChangePeptideSettings(peptideSettings)));
            }

            if (!ContainsFastaContent) // The user didn't specify any FASTA content
            {
                var docCurrent = SkylineWindow.DocumentUI;
                // If the document has precursor transitions already, then just trust the user
                // knows what they are doing, and this document is already set up for MS1 filtering
                if (HasPrecursorTransitions(docCurrent))
                    return true;

                if (docCurrent.PeptideCount == 0)
                {
                    MessageDlg.Show(WizardForm, TextUtil.LineSeparate(Resources.ImportFastaControl_ImportFasta_The_document_does_not_contain_any_peptides_,
                                                                      Resources.ImportFastaControl_ImportFasta_Please_import_FASTA_to_add_peptides_to_the_document_));
                    return false;
                }

                if (MessageBox.Show(WizardForm, TextUtil.LineSeparate(Resources.ImportFastaControl_ImportFasta_The_document_does_not_contain_any_precursor_transitions_,
                                                                      Resources.ImportFastaControl_ImportFasta_Would_you_like_to_change_the_document_settings_to_automatically_pick_the_precursor_transitions_specified_in_the_full_scan_settings_),
                                    Program.Name, MessageBoxButtons.OKCancel) != DialogResult.OK)
                    return false;

                SkylineWindow.ModifyDocument(Resources.ImportFastaControl_ImportFasta_Change_settings_to_add_precursors, doc => ChangeAutoManageChildren(doc, PickLevel.transitions, true));
            }
            else // The user specified some FASTA content
            {
                // If the user is about to add any new transitions by importing
                // FASTA, set FragmentType='p' and AutoSelect=true
                var docCurrent = SkylineWindow.Document;
                var docNew = docCurrent;
                // First preserve the state of existing document nodes in the tree
                // Todo: There are better ways to do this than this brute force method; revisit later.
                if (docNew.PeptideGroupCount > 0)
                    docNew = ChangeAutoManageChildren(docNew, PickLevel.all, false);
                var pick = docNew.Settings.PeptideSettings.Libraries.Pick;
                if (pick != PeptidePick.library && pick != PeptidePick.both)
                    docNew = docNew.ChangeSettings(docNew.Settings.ChangePeptideLibraries(lib => lib.ChangePick(PeptidePick.library)));

                var nodeInsert = SkylineWindow.SequenceTree.SelectedNode as SrmTreeNode;
                IdentityPath selectedPath = nodeInsert != null ? nodeInsert.Path : null;
                int emptyPeptideGroups;

                if (!_fastaFile)
                {
                    // Import FASTA as content
                    docNew = ImportFastaHelper.AddFasta(docNew, ref selectedPath, out emptyPeptideGroups);
                    // Document will be null if there was an error
                    if (docNew == null)
                        return false;
                }
                else
                {
                    // Import FASTA as file
                    var importer = new FastaImporter(docNew, false);
                    var fastaPath = tbxFasta.Text;
                    try
                    {
                        using (TextReader reader = File.OpenText(fastaPath))
                        using (var longWaitDlg = new LongWaitDlg(SkylineWindow) { Text = Resources.ImportFastaControl_ImportFasta_Insert_FASTA })
                        {
                            IdentityPath to = selectedPath;
                            var docImportFasta = docNew;
                            longWaitDlg.PerformWork(WizardForm, 1000, longWaitBroker =>
                                {
                                    IdentityPath nextAdd;
                                    docImportFasta = docImportFasta.AddPeptideGroups(importer.Import(reader, longWaitBroker, Helpers.CountLinesInFile(fastaPath)), false, to, out selectedPath, out nextAdd);
                                });
                            docNew = docImportFasta;
                        }
                        emptyPeptideGroups = importer.EmptyPeptideGroupCount;
                    }
                    catch (Exception x)
                    {
                        MessageDlg.Show(this, string.Format(Resources.SkylineWindow_ImportFastaFile_Failed_reading_the_file__0__1__,
                                                            fastaPath, x.Message));
                        return false;
                    }
                }
                
                // Check for empty proteins
                docNew = ImportFastaHelper.HandleEmptyPeptideGroups(WizardForm, emptyPeptideGroups, docNew);
                // Document will be null if user was given option to keep or remove empty proteins and pressed cancel
                if (docNew == null)
                    return false;

                SkylineWindow.ModifyDocument(Resources.ImportFastaControl_ImportFasta_Insert_FASTA, doc =>
                {
                    if (!ReferenceEquals(doc, docCurrent))
                        throw new InvalidDataException(Resources.SkylineWindow_ImportFasta_Unexpected_document_change_during_operation);
                    return docNew;
                });

                if (!VerifyAtLeastOnePrecursorTransition(SkylineWindow.Document))
                    return false;
            }

            return true;
        }

        private void clearBtn_Click(object sender, EventArgs e)
        {
            tbxFasta.Clear();
            _fastaFile = false;
        }

        private void enzyme_SelectedIndexChanged(object sender, EventArgs e)
        {
            _driverEnzyme.SelectedIndexChangedEvent(sender, e);
        }
    }
}