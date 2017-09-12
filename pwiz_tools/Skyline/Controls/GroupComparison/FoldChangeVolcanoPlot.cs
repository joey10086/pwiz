﻿/*
 * Original author: Tobias Rohde <tobiasr .at. uw.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2017 University of Washington - Seattle, WA
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
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using pwiz.Common.Collections;
using pwiz.Common.DataBinding;
using pwiz.Common.DataBinding.Controls;
using pwiz.Common.DataBinding.Internal;
using pwiz.Common.SystemUtil;
using pwiz.Skyline.Controls.SeqNode;
using pwiz.Skyline.Model;
using pwiz.Skyline.Model.Databinding;
using pwiz.Skyline.Model.Databinding.Entities;
using pwiz.Skyline.Model.GroupComparison;
using pwiz.Skyline.Properties;
using pwiz.Skyline.Util;
using ZedGraph;

namespace pwiz.Skyline.Controls.GroupComparison
{
    public partial class FoldChangeVolcanoPlot : FoldChangeForm, ITipDisplayer
    {
        private const int MAX_SELECTED = 100;
        private const float SELECTED_SIZE = 10.0f;
        private const float OUT_SIZE = 9.0f;
        private const float IN_SIZE = 8.0f;
        private const double MIN_PVALUE = 1E-6;

        private readonly FontSpec _font;

        private BindingListSource _bindingListSource;
        private SkylineWindow _skylineWindow;
        private bool _updatePending;

        private LineItem _foldChangeCutoffLine1;
        private LineItem _foldChangeCutoffLine2;
        private LineItem _minPValueLine;

        private readonly List<LineItem> _points;
        private readonly List<Selection> _selections;

        private FoldChangeBindingSource.FoldChangeRow _selectedRow;

        private NodeTip _tip;
        private RowFilter.ColumnFilter _absLog2FoldChangeFilter;
        private RowFilter.ColumnFilter _pValueFilter;

        public FoldChangeVolcanoPlot()
        {
            InitializeComponent();
            
            zedGraphControl.GraphPane.Title.Text = null;
            zedGraphControl.MasterPane.Border.IsVisible = false;
            zedGraphControl.GraphPane.Border.IsVisible = false;
            zedGraphControl.GraphPane.Chart.Border.IsVisible = false;
            zedGraphControl.GraphPane.Chart.Border.IsVisible = false;

            zedGraphControl.GraphPane.XAxis.Title.Text = GroupComparisonStrings.FoldChange_Log2_Fold_Change_;
            zedGraphControl.GraphPane.YAxis.Title.Text = GroupComparisonStrings.FoldChange__Log10_P_Value_;
            zedGraphControl.GraphPane.X2Axis.IsVisible = false;
            zedGraphControl.GraphPane.Y2Axis.IsVisible = false;
            zedGraphControl.GraphPane.XAxis.MajorTic.IsOpposite = false;
            zedGraphControl.GraphPane.YAxis.MajorTic.IsOpposite = false;
            zedGraphControl.GraphPane.XAxis.MinorTic.IsOpposite = false;
            zedGraphControl.GraphPane.YAxis.MinorTic.IsOpposite = false;
            zedGraphControl.GraphPane.IsFontsScaled = false;
            zedGraphControl.GraphPane.YAxis.Scale.MaxGrace = 0.1;
            zedGraphControl.IsZoomOnMouseCenter = true;

            _points = new List<LineItem>();
            _selections = new List<Selection>();

            _font = new FontSpec("Arial", 12.0f, Color.Red, false, false, false, Color.Empty, null, FillType.None) // Not L10N
            {
                Border = { IsVisible = false }
            };
        }

        private void AdjustLocations(GraphPane pane)
        {
            pane.YAxis.Scale.Min = 0.0;

            if (_foldChangeCutoffLine1 == null || _foldChangeCutoffLine2 == null || _minPValueLine == null)
                return;

            _foldChangeCutoffLine1[0].Y = _foldChangeCutoffLine2[0].Y = pane.YAxis.Scale.Min;
            _foldChangeCutoffLine1[1].Y = _foldChangeCutoffLine2[1].Y = pane.YAxis.Scale.Max;
            _minPValueLine[0].X = pane.XAxis.Scale.Min;
            _minPValueLine[1].X = pane.XAxis.Scale.Max;

            foreach (var selection in _selections)
                if (selection.Label != null)
                    selection.Label.Location.Y = selection.Point.Y + SELECTED_SIZE / 2.0f / pane.Rect.Height * (pane.YAxis.Scale.Max - pane.YAxis.Scale.Min);
        }

        private void GraphPane_AxisChangeEvent(GraphPane pane)
        {
            AdjustLocations(pane);
        }

        private void zedGraphControl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
                Program.MainWindow.FocusDocument();
        }

        public override string GetTitle(string groupComparisonName)
        {
            return base.GetTitle(groupComparisonName) + ':' + GroupComparisonStrings.FoldChangeVolcanoPlot_GetTitle_Volcano_Plot;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (FoldChangeBindingSource != null)
            {
                AllowDisplayTip = true;

                _bindingListSource = FoldChangeBindingSource.GetBindingListSource();
                _bindingListSource.ListChanged += BindingListSourceOnListChanged;
                _bindingListSource.AllRowsChanged += BindingListSourceAllRowsChanged;
                zedGraphControl.GraphPane.AxisChangeEvent += GraphPane_AxisChangeEvent;

                if (_skylineWindow == null)
                {
                    _skylineWindow = ((SkylineDataSchema)_bindingListSource.ViewInfo.DataSchema).SkylineWindow;
                    if (_skylineWindow != null)
                    {
                        _skylineWindow.SequenceTree.AfterSelect += SequenceTreeOnAfterSelect;
                    }
                }

                UpdateGraph(Settings.Default.FilterVolcanoPlotPoints);
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            AllowDisplayTip = false;

            if (_skylineWindow != null && _skylineWindow.SequenceTree != null)
            {
                _skylineWindow.SequenceTree.AfterSelect -= SequenceTreeOnAfterSelect;
                _skylineWindow = null;
            }

            zedGraphControl.GraphPane.AxisChangeEvent -= GraphPane_AxisChangeEvent;
            _bindingListSource.AllRowsChanged -= BindingListSourceAllRowsChanged;
            _bindingListSource.ListChanged -= BindingListSourceOnListChanged;

            UpdateFilter(false);

            base.OnHandleDestroyed(e);
        }

        private void SequenceTreeOnAfterSelect(object sender, TreeViewEventArgs treeViewEventArgs)
        {
            QueueUpdateGraph();
        }

        public void QueueUpdateGraph()
        {
            if (!IsHandleCreated)
                return;

            if (!_updatePending)
            {
                _updatePending = true;
                BeginInvoke(new Action(() =>
                {
                    _updatePending = false;
                    UpdateGraph(Settings.Default.FilterVolcanoPlotPoints);
                }));
            }
        }

        public static bool CutoffSettingsValid
        {
            get
            {
                return !double.IsNaN(Settings.Default.Log2FoldChangeCutoff) &&
                       !double.IsNaN(Settings.Default.PValueCutoff) &&
                       Settings.Default.Log2FoldChangeCutoff != 0.0 &&
                       Settings.Default.PValueCutoff >= 0.0;
            }
        }

        private class Selection
        {
            public Selection(PointPair point, TextObj label)
            {
                Point = point;
                Label = label;
            }

            public PointPair Point { get; private set; }
            public TextObj Label { get; private set; }
        }

        private void UpdateGraph()
        {
            if (!IsHandleCreated)
                return;
            
            zedGraphControl.GraphPane.GraphObjList.Clear();
            zedGraphControl.GraphPane.CurveList.Clear();
            _points.Clear();
            _selections.Clear();
            _foldChangeCutoffLine1 = _foldChangeCutoffLine2 = _minPValueLine = null;

            var rows = _bindingListSource.OfType<RowItem>()
                .Select(rowItem => rowItem.Value)
                .OfType<FoldChangeBindingSource.FoldChangeRow>()
                .ToArray();

            var selectedPoints = new PointPairList();
            var outPoints = new PointPairList();
            var inPoints = new PointPairList();

            var count = 0;

            // Create points and Selection objects
            foreach (var row in rows.OrderBy(r => r.FoldChangeResult.AdjustedPValue))
            {
                var foldChange = row.FoldChangeResult.Log2FoldChange;
                var pvalue = -Math.Log10(Math.Max(MIN_PVALUE, row.FoldChangeResult.AdjustedPValue));

                var point = new PointPair(foldChange, pvalue) { Tag = row };
                if (Settings.Default.GroupComparisonShowSelection && count < MAX_SELECTED && IsSelected(row))
                {
                    selectedPoints.Add(point);
                    var textObj = CreateSelectionLabel(point);
                    _selections.Add(new Selection(point, textObj));
                    ++count;
                }
                else if (!CutoffSettingsValid || pvalue > Settings.Default.PValueCutoff && row.FoldChangeResult.AbsLog2FoldChange > Math.Abs(Settings.Default.Log2FoldChangeCutoff))
                    outPoints.Add(point);
                else
                    inPoints.Add(point);
            }

            // The order matters here, selected points should be highest in the zorder, followed by out points and in points
            AddPoints(selectedPoints, Color.Red, SELECTED_SIZE);
            AddPoints(outPoints, Color.Blue, OUT_SIZE);
            AddPoints(inPoints, Color.Gray, IN_SIZE);

            if (CutoffSettingsValid)
            {
                // Insert after selected items, but before all other items

                int index = 1;
                // The coordinates that depened on the axis scale dont matter here, the AxisChangeEvent will fix those
                _foldChangeCutoffLine1 = CreateAndInsert(index++, Settings.Default.Log2FoldChangeCutoff, Settings.Default.Log2FoldChangeCutoff, 0.0, 0.0);
                _foldChangeCutoffLine2 = CreateAndInsert(index++, - Settings.Default.Log2FoldChangeCutoff, -Settings.Default.Log2FoldChangeCutoff, 0.0, 0.0);
                _minPValueLine = CreateAndInsert(index, 0.0, 0.0, Settings.Default.PValueCutoff, Settings.Default.PValueCutoff);
            }

            zedGraphControl.GraphPane.YAxis.Scale.Min = 0.0;
            zedGraphControl.GraphPane.XAxis.Scale.MinAuto = zedGraphControl.GraphPane.XAxis.Scale.MaxAuto = zedGraphControl.GraphPane.YAxis.Scale.MaxAuto = true;       
            zedGraphControl.GraphPane.AxisChange();
            zedGraphControl.GraphPane.XAxis.Scale.MinAuto = zedGraphControl.GraphPane.XAxis.Scale.MaxAuto = zedGraphControl.GraphPane.YAxis.Scale.MaxAuto = false;

            zedGraphControl.Invalidate();
        }

        private TextObj CreateSelectionLabel(PointPair point)
        {
            var row = point.Tag as FoldChangeBindingSource.FoldChangeRow;
            if (row == null)
                return null;

            string text;
            if (row.Peptide != null)
                text = row.Peptide.ModifiedSequence == null ? row.Peptide.ToString() : row.Peptide.ModifiedSequence.ToString();
            else if (row.Protein != null)
                text = PeptideGroupTreeNode.ProteinModalDisplayText(row.Protein.DocNode);
            else
                return null;

            var textObj = new TextObj(text, point.X, point.Y, CoordType.AxisXYScale, AlignH.Center, AlignV.Bottom)
            {
                IsClippedToChartRect = true,
                FontSpec = _font,
                ZOrder = ZOrder.A_InFront
            };

            zedGraphControl.GraphPane.GraphObjList.Add(textObj);

            return textObj;
        }

        private void AddPoints(PointPairList points, Color color, float size)
        {
            var inPointsLineItem = new LineItem(null, points, Color.Black, SymbolType.Circle)
            {
                Line = { IsVisible = false },
                Symbol = { Border = { IsVisible = false }, Fill = new Fill(color), Size = size, IsAntiAlias = true }
            };

            _points.Add(inPointsLineItem);
            zedGraphControl.GraphPane.CurveList.Add(inPointsLineItem);
        }

        private LineItem CreateAndInsert(int index, double fromX, double toX, double fromY, double toY)
        {
            var item = CreateLineItem(null, fromX, toX, fromY, toY, Color.Black);
            zedGraphControl.GraphPane.CurveList.Insert(index, item);

            return item;
        }

        private LineItem CreateLineItem(string text, double fromX, double toX, double fromY, double toY, Color color)
        {
            return new LineItem(text, new[] { fromX, toX }, new[] { fromY, toY }, color, SymbolType.None, 1.0f)
            {
                Line = { Style = DashStyle.Dash }
            };
        }

        private void BindingListSourceAllRowsChanged(object sender, EventArgs e)
        {
            QueueUpdateGraph();
        }

        private void BindingListSourceOnListChanged(object sender, ListChangedEventArgs listChangedEventArgs)
        {
            QueueUpdateGraph();
        }

        private bool zedGraphControl_MouseMoveEvent(ZedGraphControl sender, MouseEventArgs e)
        {
            return MoveMouse(e.Button, e.Location);
        }

        #region Selection Code

        public bool MoveMouse(MouseButtons buttons, Point point)
        {
            // Pan
            if (ModifierKeys.HasFlag(Keys.Control) && buttons.HasFlag(MouseButtons.Left))
                AdjustLocations(zedGraphControl.GraphPane);

            CurveItem nearestCurveItem = null;
            var index = -1;
            if (TryGetNearestCurveItem(point, ref nearestCurveItem, ref index))
            {
                var lineItem = nearestCurveItem as LineItem;
                if (lineItem == null || index < 0 || index >= lineItem.Points.Count || lineItem[index].Tag == null)
                    return false;

                _selectedRow = (FoldChangeBindingSource.FoldChangeRow) lineItem[index].Tag;
                zedGraphControl.Cursor = Cursors.Hand;

                if (_tip == null)
                    _tip = new NodeTip(this);

                _tip.SetTipProvider(new FoldChangeRowTipProvider(_selectedRow), new Rectangle(point, new Size()),
                    point);

                return true;
            }
            else
            {
                if (_tip != null)
                    _tip.HideTip();

                _selectedRow = null;
                return false;
            }
        }

        private bool TryGetNearestCurveItem(Point point, ref CurveItem nearestCurveItem, ref int index)
        {
            foreach (var item in _points)
            {
                if (zedGraphControl.GraphPane.FindNearestPoint(point, item, out nearestCurveItem, out index))
                    return true;
            }
            return false;
        }

        public void Select(IdentityPath identityPath)
        {
            var skylineWindow = _skylineWindow;
            if (skylineWindow == null)
                return;

            var alreadySelected = IsPathSelected(skylineWindow.SelectedPath, identityPath);
            if (alreadySelected)
                skylineWindow.SequenceTree.SelectedNode = null;

            skylineWindow.SelectedPath = identityPath;
            skylineWindow.UpdateGraphPanes();
        }

        public void MultiSelect(IdentityPath identityPath)
        {
            var skylineWindow = _skylineWindow;
            if (skylineWindow == null)
                return;

            var list = skylineWindow.SequenceTree.SelectedPaths;
            if (GetSelectedPath(identityPath) == null)
            {
                list.Insert(0, identityPath);
                skylineWindow.SequenceTree.SelectedPaths = list;
                if (!IsPathSelected(skylineWindow.SelectedPath, identityPath))
                    skylineWindow.SequenceTree.SelectPath(identityPath);
            }
            skylineWindow.UpdateGraphPanes();
        }

        public void Deselect(IdentityPath identityPath)
        {
            var skylineWindow = _skylineWindow;
            if (skylineWindow == null)
                return;

            var list = skylineWindow.SequenceTree.SelectedPaths.ToList();
            var selectedPath = GetSelectedPath(identityPath);
            if (selectedPath != null)
            {
                if (selectedPath.Depth < identityPath.Depth)
                {
                    var protein = (PeptideGroupDocNode) skylineWindow.DocumentUI.FindNode(selectedPath);
                    var peptide = (PeptideDocNode) skylineWindow.DocumentUI.FindNode(identityPath);

                    var peptides = protein.Peptides.Except(new[] { peptide });
                    list.Remove(selectedPath);
                    list.AddRange(peptides.Select(p => new IdentityPath(selectedPath, p.Id)));
                }
                else if (selectedPath.Depth > identityPath.Depth)
                {
                    var protein = (PeptideGroupDocNode) skylineWindow.DocumentUI.FindNode(identityPath);
                    var peptidePaths = protein.Peptides.Select(p => new IdentityPath(identityPath, p.Id));

                    list = list.Except(list.Where(path => peptidePaths.Contains(path))).ToList();
                }
                else
                {
                    list.Remove(identityPath);
                }

                skylineWindow.SequenceTree.SelectedPaths = list;

                if (list.Any())
                {
                    if (Equals(skylineWindow.SelectedPath, selectedPath))
                        skylineWindow.SequenceTree.SelectPath(list.First());
                    else
                        skylineWindow.SequenceTree.Refresh();
                }
                else
                {
                    skylineWindow.SequenceTree.SelectedNode = null;
                }
            }
            skylineWindow.UpdateGraphPanes();
        }

        private bool IsSelected(FoldChangeBindingSource.FoldChangeRow row)
        {
            var docNode = row.Peptide ?? (SkylineDocNode)row.Protein;
            return _skylineWindow != null && GetSelectedPath(docNode.IdentityPath) != null;
        }

        public IdentityPath GetSelectedPath(IdentityPath identityPath)
        {
            var skylineWindow = _skylineWindow;
            return skylineWindow != null ? skylineWindow.SequenceTree.SelectedPaths.FirstOrDefault(p => IsPathSelected(p, identityPath)) : null;
        }

        public bool IsPathSelected(IdentityPath selectedPath, IdentityPath identityPath)
        {
            return selectedPath != null && identityPath != null &&
                selectedPath.Depth <= (int)SrmDocument.Level.Molecules && identityPath.Depth <= (int)SrmDocument.Level.Molecules &&
                (selectedPath.Depth >= identityPath.Depth && Equals(selectedPath.GetPathTo(identityPath.Depth), identityPath) ||
                selectedPath.Depth <= identityPath.Depth && Equals(identityPath.GetPathTo(selectedPath.Depth), selectedPath));
        }

        private bool zedGraphControl_MouseDownEvent(ZedGraphControl sender, MouseEventArgs e)
        {
            return e.Button.HasFlag(MouseButtons.Left) && ClickSelectedRow();
        }

        public bool ClickSelectedRow()
        {
            if (_selectedRow == null)
                return false;

            SkylineDocNode docNode = null;
            if (_selectedRow.Peptide != null)
                docNode = _selectedRow.Peptide;
            else if (_selectedRow.Protein != null)
                docNode = _selectedRow.Protein;

            if (docNode == null || ModifierKeys.HasFlag(Keys.Shift))
                return false;

            var isSelected = IsSelected(_selectedRow);
            var ctrl = ModifierKeys.HasFlag(Keys.Control);

            if (!ctrl)
            {
                Select(docNode.IdentityPath);
                return true; // No need to call UpdateGraph
            }
            else if (isSelected)
                Deselect(docNode.IdentityPath);
            else
                MultiSelect(docNode.IdentityPath);

            FormUtil.OpenForms.OfType<FoldChangeVolcanoPlot>().ForEach(v => v.QueueUpdateGraph()); // Update all volcano plots
            return true;
        }

        #endregion

        private void zedGraphControl_ContextMenuBuilder(ZedGraphControl sender, ContextMenuStrip menuStrip, Point mousePt, ZedGraphControl.ContextMenuObjectState objState)
        {
            BuildContextMenu(sender, menuStrip, mousePt, objState);
        }

        protected override void BuildContextMenu(ZedGraphControl sender, ContextMenuStrip menuStrip, Point mousePt, ZedGraphControl.ContextMenuObjectState objState)
        {
            base.BuildContextMenu(sender, menuStrip, mousePt, objState);

            // Find the first seperator
            var index = menuStrip.Items.OfType<ToolStripItem>().ToArray().IndexOf(t => t is ToolStripSeparator);

            if (index >= 0)
            {
                menuStrip.Items.Insert(index++, new ToolStripSeparator());
                menuStrip.Items.Insert(index++, new ToolStripMenuItem(GroupComparisonStrings.FoldChangeVolcanoPlot_BuildContextMenu_Properties, null, OnPropertiesClick));
                menuStrip.Items.Insert(index, new ToolStripMenuItem(GroupComparisonStrings.FoldChangeVolcanoPlot_BuildContextMenu_Selection, null, OnSelectionClick)
                    { Checked = Settings.Default.GroupComparisonShowSelection });
            }
        }

        private void OnSelectionClick(object o, EventArgs eventArgs)
        {
            Settings.Default.GroupComparisonShowSelection = !Settings.Default.GroupComparisonShowSelection;
            UpdateGraph();
        }

        public void ShowProperties()
        {
            using (var dlg = new VolcanoPlotProperties())
            {
                dlg.ShowDialog();
            }
        }

        private void OnPropertiesClick(object o, EventArgs eventArgs)
        {
            ShowProperties();
        }

        public void UpdateGraph(bool filter)
        {
            if (!UpdateFilter(filter))
                UpdateGraph();
        }

        private bool UpdateFilter(bool filter)
        {
            if (!IsHandleCreated)
                return false;

            if (!_bindingListSource.IsComplete)
                return true;

            var columnFilters = _bindingListSource.RowFilter.ColumnFilters.ToList();
            var columns = _bindingListSource.ViewSpec.Columns;

            var absLog2FCExists = columns.Any(c => c.Name == "FoldChangeResult.AbsLog2FoldChange"); // Not L10N
            var pValueExists = columns.Any(c => c.Name == "FoldChangeResult.AdjustedPValue"); // Not L10N

            if (filter && ValidFilterCount(columnFilters) == 2 && absLog2FCExists && pValueExists || !filter && ValidFilterCount(columnFilters) == 0 && !absLog2FCExists)
                return false;
        
            columnFilters.Remove(_absLog2FoldChangeFilter);
            columnFilters.Remove(_pValueFilter);

            if (CutoffSettingsValid && filter)
            {
                var missingColumns = new List<ColumnSpec>();
                if (!absLog2FCExists)
                    missingColumns.Add(new ColumnSpec(PropertyPath.Root.Property("FoldChangeResult").Property("AbsLog2FoldChange"))); // Not L10N
                if (!pValueExists)
                    missingColumns.Add(new ColumnSpec(PropertyPath.Root.Property("FoldChangeResult").Property("AdjustedPValue"))); // Not L10N

                if (!absLog2FCExists || !pValueExists)
                    SetColumns(_bindingListSource.ViewSpec.Columns.Concat(missingColumns));

                _absLog2FoldChangeFilter = CreateColumnFilter(ColumnCaptions.AbsLog2FoldChange, FilterOperations.OP_IS_GREATER_THAN, Settings.Default.Log2FoldChangeCutoff); // Not L10N
                _pValueFilter = CreateColumnFilter(ColumnCaptions.AdjustedPValue, FilterOperations.OP_IS_LESS_THAN, Math.Pow(10, -Settings.Default.PValueCutoff)); // Not L10N

                if (_absLog2FoldChangeFilter != null && _pValueFilter != null)
                {
                    columnFilters.Clear();
                    columnFilters.Add(_absLog2FoldChangeFilter);
                    columnFilters.Add(_pValueFilter);  
                }
            }
            else
            {
                // Remove AbsLog2FoldChange column
                SetColumns(columns.Except(columns.Where(c => c.Name == "FoldChangeResult.AbsLog2FoldChange"))); // Not L10N
            }

            _bindingListSource.RowFilter = _bindingListSource.RowFilter.SetColumnFilters(columnFilters);
            return true;
        }

        private int ValidFilterCount(IList<RowFilter.ColumnFilter> filters)
        {
            return ContainsFilter(filters, ColumnCaptions.AbsLog2FoldChange, FilterOperations.OP_IS_GREATER_THAN, Settings.Default.Log2FoldChangeCutoff) +
                   ContainsFilter(filters, ColumnCaptions.AdjustedPValue, FilterOperations.OP_IS_LESS_THAN, Math.Pow(10, -Settings.Default.PValueCutoff));
        }

        // Returns 1 if the filter is found, 0 otherwise
        int ContainsFilter(IEnumerable<RowFilter.ColumnFilter> filters, string columnCaption, IFilterOperation filterOp, double operand)
        {
            return filters.Contains(f => f.ColumnCaption == columnCaption &&
                                         ReferenceEquals(f.Predicate.FilterOperation, filterOp) &&
                                         Equals(f.Predicate.GetOperandDisplayText(_bindingListSource.ViewInfo.DataSchema, typeof(double)), operand.ToString(CultureInfo.CurrentCulture))) ? 1 : 0;
        }

        private void SetColumns(IEnumerable<ColumnSpec> columns)
        {
            _bindingListSource.SetViewContext(_bindingListSource.ViewContext,
                new ViewInfo(_bindingListSource.ViewInfo.DataSchema,
                    typeof(FoldChangeBindingSource.FoldChangeRow),
                    _bindingListSource.ViewSpec.SetColumns(columns)));
        }

        private RowFilter.ColumnFilter CreateColumnFilter(string columnDisplayName, IFilterOperation filterOp, double operand)
        {
            var op = FilterPredicate.CreateFilterPredicate(_bindingListSource.ViewInfo.DataSchema,
                typeof(double), filterOp,
                operand.ToString(CultureInfo.CurrentCulture));

            return new RowFilter.ColumnFilter(columnDisplayName, op);
        }

        public Rectangle ScreenRect { get { return  Screen.GetBounds(this); } }
        public bool AllowDisplayTip { get; private set; }
        public Rectangle RectToScreen(Rectangle r)
        {
            return RectangleToScreen(r);
        }

        #region Functional Test Support

        public bool UseOverridenKeys { get; set; }
        public Keys OverridenModifierKeys { get; set; }
        private new Keys ModifierKeys
        {
            get { return UseOverridenKeys ? OverridenModifierKeys : Control.ModifierKeys; }
        }

        public bool UpdatePending { get { return _updatePending; } }

        public FoldChangeBindingSource.FoldChangeRow GetSelectedRow()
        {
            return zedGraphControl.GraphPane.CurveList[0].Points[0].Tag as FoldChangeBindingSource.FoldChangeRow;
        }

        public Point GraphToScreenCoordinates(double x, double y)
        {
            var pt = new PointF((float)x, (float)y);
            pt = zedGraphControl.GraphPane.GeneralTransform(pt, CoordType.AxisXYScale);
            return new Point((int)pt.X, (int)pt.Y);
        }

        public CurveCounts GetCurveCounts()
        {
            var index = CutoffSettingsValid ? 4 : 1;
            var curveList = zedGraphControl.GraphPane.CurveList;
            return new CurveCounts(curveList.Count, curveList[0].Points.Count,
                curveList[index++].Points.Count,
                curveList[index].Points.Count);

        }
            
        public class CurveCounts
        {
            public CurveCounts(int curveCount, int selectedCount, int outCount, int inCount)
            {
                CurveCount = curveCount;
                SelectedCount = selectedCount;
                OutCount = outCount;
                InCount = inCount;
            }

            public int CurveCount { get; private set; }
            public int SelectedCount { get; private set; }
            public int OutCount { get; private set; }
            public int InCount { get; private set; }
        }

        #endregion
    }
}