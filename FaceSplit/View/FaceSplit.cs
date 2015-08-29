﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;
using System.Xml.Serialization;
using System.Runtime.InteropServices;
using FaceSplit.Model;
using System.IO;
using Shortcut;
using FaceSplit.Properties;

namespace FaceSplit
{
    public partial class FaceSplit : Form
    {
        /// <summary>
        /// Default height and width.
        /// </summary>
        public const int DEFAULT_WIDTH = 250;
        public const int DEFAULT_HEIGHT = 38;

        public const int ZERO = 0;

        public const int SEGMENT_HEIGHT = 15;

        public int splitY_start;
        /// <summary>
        /// When you unsplit, the segment timer has to be set to the actual time since you did that split.
        /// </summary>
        public double timeElapsedSinceSplit;


        Split split;
        DisplayMode displayMode;
        List<Information> informations;

        Model.LayoutSettings layoutSettings;

        SaveFileDialog saveRunDialog;
        SaveFileDialog saveLayoutDialog;

        OpenFileDialog openRunDialog;
        OpenFileDialog openLayoutDialog;

        XmlSerializer serializer;

        /// <summary>
        /// The watch on the screen.
        /// </summary>
        Stopwatch watch;
        /// <summary>
        /// The watch for segments.
        /// </summary>
        Stopwatch segmentWatch;
        /// <summary>
        /// Use when the run is done but you want to unsplit.
        /// We keep the timer going but we show the time when you split on the last split.
        /// Same for segment timer.
        /// </summary>
        TimeSpan runTimeOnCompletionPause;
        double segmentTimeOnCompletionPause;

        Color watchColor;
        Color segmentWatchColor;

        /// <summary>
        /// Rectangle for each segments.
        /// </summary>
        List<Rectangle> segmentsRectangles;

        /// <summary>
        /// The rectangle for the watch.
        /// </summary>
        Rectangle watchRectangle;

        private Hotkey hotkeyTrigger = new Hotkey(Modifiers.None, Keys.Add);
        private HotkeyBinder hotkeyBinder;
        private bool globalHotkeysActive;

        /// <summary>
        /// Code starting from here is for moving the window without having a border.
        /// </summary>
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        [DllImportAttribute("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd,
                         int Msg, int wParam, int lParam);
        [DllImportAttribute("user32.dll")]
        public static extern bool ReleaseCapture();
        //End of the code for the window moving.

        /// <summary>
        /// Initial constructor.
        /// </summary>
        public FaceSplit()
        {
            InitializeComponent();
            ConfigureFilesDialogs();
            layoutSettings = new Model.LayoutSettings();
            if (!Settings.Default.LayoutSettingsFile.Equals(""))
            {
                layoutSettings.File = Settings.Default.LayoutSettingsFile;
            }
            hotkeyBinder = new HotkeyBinder();
            BindHotkeys();
            globalHotkeysActive = true;
            segmentsRectangles = new List<Rectangle>();
            watchRectangle = new Rectangle(ZERO, ZERO, DEFAULT_WIDTH, DEFAULT_HEIGHT);
            displayMode = DisplayMode.TIMER_ONLY;
            watchColor = Settings.Default.TimerNotRunningColor;
            segmentWatchColor = Settings.Default.SegmentTimerNotRunningColor;
            informations = new List<Information>();
            splitY_start = 0;
            base.Paint += new PaintEventHandler(DrawFaceSplit);
            base.Size = new Size(DEFAULT_WIDTH, DEFAULT_HEIGHT);
            base.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            watch = new Stopwatch();
            segmentWatch = new Stopwatch();
            ticksTimer.Enabled = true;
        }

        private void BindHotkeys()
        {
            hotkeyBinder.Bind(Modifiers.None, Keys.Space).To(KeyboardSplit);
            hotkeyBinder.Bind(Modifiers.None, Keys.Multiply).To(KeyboardReset);
            hotkeyBinder.Bind(Modifiers.None, Keys.Subtract).To(KeyboardUnsplit);
            hotkeyBinder.Bind(Modifiers.None, Keys.Divide).To(KeyboardSkip);
            hotkeyBinder.Bind(Modifiers.None, Keys.Decimal).To(KeyboardPause);
            if (!hotkeyBinder.IsHotkeyAlreadyBound(hotkeyTrigger))
            {
                hotkeyBinder.Bind(hotkeyTrigger).To(ToggleGlobalHotkeys);
            }
        }

        private void UnbindHotkeys()
        {
            hotkeyBinder.Unbind(Modifiers.None, Keys.Space);
            hotkeyBinder.Unbind(Modifiers.None, Keys.Multiply);
            hotkeyBinder.Unbind(Modifiers.None, Keys.Subtract);
            hotkeyBinder.Unbind(Modifiers.None, Keys.Divide);
            hotkeyBinder.Unbind(Modifiers.None, Keys.Decimal);
        }

        private void ToggleGlobalHotkeys()
        {
            globalHotkeysActive = !globalHotkeysActive;
            if (globalHotkeysActive)
            {
                BindHotkeys();
                Icon = Properties.Resources.hotkeysOn;
            }
            else
            {
                UnbindHotkeys();
                Icon = Properties.Resources.hotkeysOff;
            }
        }

        /// <summary>
        /// Function executed by the timer on each tick.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void TimerTicks(object sender, EventArgs e)
        {
            Invalidate();
            if (displayMode == DisplayMode.SEGMENTS)
            {
                UpdateInformationsData();
            }
        }


        private void mnuEditRun_Click(object sender, EventArgs e)
        {
            string splitFile = (split == null) ? null : split.File;
            int runsCompleted = (split == null) ? 0 : split.RunsCompleted;
            RunEditor runEditor = new RunEditor(split);
            runEditor.ShowDialog();
            if (runEditor.DialogResult == DialogResult.OK)
            {
                string runTitle = runEditor.Split.RunTitle;
                string runGoal = runEditor.Split.RunGoal;
                int attemptsCount = runEditor.Split.AttemptsCount;
                List<Segment> segments = runEditor.Split.Segments;
                split = new Split(runTitle, runGoal, attemptsCount, segments);
                split.File = splitFile;
                split.RunsCompleted = runsCompleted;
                informations.Clear();
                FillInformations();
                CreateSegmentsRectangles();
                displayMode = DisplayMode.SEGMENTS;
            }
        }

        private void mnuSaveRun_Click(object sender, EventArgs e)
        {
            if (split != null)
            {
                if (split.File == null)
                {
                    SaveRunToFileAs();
                }
                else
                {
                    SaveRunToFile();
                }
                
            }
        }

        private void mnuSaveRunAs_Click(object sender, EventArgs e)
        {
            SaveRunToFileAs();
        }

        private void mnuLoadRun_Click(object sender, EventArgs e)
        {
            if (openRunDialog.ShowDialog() == DialogResult.OK)
            {
                LoadFile(openRunDialog.FileName);
            }
        }

        private void mnuEditLayout_Click(object sender, EventArgs e)
        {
            LayoutSettingsEditor layoutSettings = new LayoutSettingsEditor();
            if (layoutSettings.ShowDialog() == DialogResult.OK)
            {
                Settings.Default.Save();
            }
            else
            {
                Settings.Default.Reload();
            }
        }

        private void mnuSaveLayout_Click(object sender, EventArgs e)
        {
            if (layoutSettings.File == null)
            {
                SaveLayoutToFileAs();
            }
            else
            {
                SaveLayoutToFile();
            }
        }

        private void mnuSaveLayoutAs_Click(object sender, EventArgs e)
        {
            SaveLayoutToFileAs();
        }


        private void loadLayoutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(openLayoutDialog.ShowDialog() == DialogResult.OK)
            {
                LoadLayoutFromFile(openLayoutDialog.FileName);
            }
        }

        private void mnuResetLayout_Click(object sender, EventArgs e)
        {
            Settings.Default.Reset();
        }

        private void mnuCloseSplit_Click(object sender, EventArgs e)
        {
            split = null;
            displayMode = DisplayMode.TIMER_ONLY;
            watchRectangle.Y = ZERO;
            Height = DEFAULT_HEIGHT;
        }

        private void mnuExit_Click(object sender, EventArgs e)
        {
            base.Close();
        }

        private void DrawFaceSplit(object sender, PaintEventArgs e)
        {
            DrawWatch(e.Graphics);
            if (displayMode == DisplayMode.SEGMENTS)
            {                
                DrawInformations(e.Graphics);
                DrawSegments(e.Graphics);
                if (split.RunStatus == RunStatus.STOPPED || split.PreviousSegmentWasSkipped())
                {
                    FillEmptySegmentTimer(e.Graphics);
                }
                else if (split.RunStatus != RunStatus.STOPPED)
                {
                    DrawSegmentTimer(e.Graphics);
                }
            }
            UpdateInformationsStyle();
        }

        private void UpdateInformationsStyle()
        {
            foreach (Information information in informations)
            {
                information.UpdateStyle();
            }
        }

        private void ConfigureFilesDialogs()
        {
            saveRunDialog = new SaveFileDialog();
            saveRunDialog.DefaultExt = ".fss";
            saveRunDialog.Filter = "FaceSplit split file (*.fss)|*.fss";
            saveRunDialog.AddExtension = true;

            saveLayoutDialog = new SaveFileDialog();
            saveLayoutDialog.DefaultExt = ".fsl";
            saveLayoutDialog.Filter = "FaceSplit layout file (*.fsl)|*.fsl";
            saveLayoutDialog.AddExtension = true;

            openRunDialog = new OpenFileDialog();
            openRunDialog.DefaultExt = ".fss";
            openRunDialog.Filter = "FaceSplit split file (*.fss)|*.fss";
            openRunDialog.AddExtension = true;

            openLayoutDialog = new OpenFileDialog();
            openLayoutDialog.DefaultExt = ".fsl";
            openLayoutDialog.Filter = "FaceSplit layout file (*.fsl)|*.fsl";
            openLayoutDialog.AddExtension = true;
        }

        private void SaveRunToFileAs()
        {           
            if (split != null)
            {
                if (saveRunDialog.ShowDialog() == DialogResult.OK)
                {
                    split.File = saveRunDialog.FileName;
                    SaveRunToFile();
                }
            }
        }

        private void SaveRunToFile()
        {
            using (StreamWriter file = new StreamWriter(split.File, false))
            {
                file.WriteLine(split.RunTitle);
                file.WriteLine(split.RunGoal);
                file.WriteLine(split.AttemptsCount);
                file.WriteLine(split.RunsCompleted);
                foreach (Segment segment in split.Segments)
                {
                    file.WriteLine(segment.SegmentName + "-" + segment.SplitTime + "-" + segment.SegmentTime + "-" + segment.BestSegmentTime);
                }
                file.Close();
            }
        }

        private void LoadFile(string fileName)
        {
            string[] lines = File.ReadAllLines(fileName);
            string runTitle = "";
            string runGoal = "";
            int runCount = 0;
            int runsCompleted = 0;
            string segmentName = "";
            string segmentSplitTime = "";
            string segmentTime = "";
            string segmentBestTime = "";
            List<Segment> segments = new List<Segment>();
            try
            {
                runTitle = lines.ElementAt(0);
                runGoal = lines.ElementAt(1);
                runCount = int.Parse(lines.ElementAt(2));
                runsCompleted = int.Parse(lines.ElementAt(3));
                for (int i = 4; i < lines.Length; ++i)
                {
                    segmentName = lines.ElementAt(i).Split('-').ElementAt(0);
                    segmentSplitTime = lines.ElementAt(i).Split('-').ElementAt(1);
                    segmentTime = lines.ElementAt(i).Split('-').ElementAt(2);
                    segmentBestTime = lines.ElementAt(i).Split('-').ElementAt(3);
                    segments.Add(new Segment(segmentName, FaceSplitUtils.TimeParse(segmentSplitTime), FaceSplitUtils.TimeParse(segmentTime), FaceSplitUtils.TimeParse(segmentBestTime)));
                }
                split = new Split(runTitle, runGoal, runCount, segments);
                split.RunsCompleted = runsCompleted;
                split.File = fileName;
                informations.Clear();
                FillInformations();
                CreateSegmentsRectangles();
                displayMode = DisplayMode.SEGMENTS;
            }
            catch (FormatException fe)
            {
                MessageBox.Show("This file was not recognize as a FaceSplit split file.");
            }
            catch (IndexOutOfRangeException iore)
            {
                MessageBox.Show("This file was not recognize as a FaceSplit split file.");
            }
        }

        private void SaveLayoutToFileAs()
        {
            if (saveLayoutDialog.ShowDialog() == DialogResult.OK)
            {
                layoutSettings.File = saveLayoutDialog.FileName;
                SaveLayoutToFile();
            }
        }

        private void SaveLayoutToFile()
        {
            layoutSettings.SaveLayoutSettings();
            Settings.Default.LayoutSettingsFile = layoutSettings.File;
            serializer = new XmlSerializer(layoutSettings.GetType());
            serializer.Serialize(new StreamWriter(layoutSettings.File, false), layoutSettings);
            Settings.Default.Save();
        }

        private void LoadLayoutFromFile(string file)
        {
            serializer = new XmlSerializer(layoutSettings.GetType());
            layoutSettings = (Model.LayoutSettings)serializer.Deserialize(new StreamReader(file));
            layoutSettings.LoadLayoutSettings();
            layoutSettings.File = file;
            Settings.Default.Save();
        }

        /// <summary>
        /// Create a rectangle for each segment and adjust the position of the clock
        /// and the heigt of FaceSplit.
        /// </summary>
        private void CreateSegmentsRectangles()
        {
            segmentsRectangles.Clear();
            int index = 0;
            foreach (Segment segment in split.Segments)
            {
                segmentsRectangles.Add(new Rectangle(ZERO, (index * SEGMENT_HEIGHT) + splitY_start, DEFAULT_WIDTH, SEGMENT_HEIGHT));
                index++;
            }
            watchRectangle.Y = segmentsRectangles.Count() * SEGMENT_HEIGHT + splitY_start;
            Height = (segmentsRectangles.Count() * SEGMENT_HEIGHT) + (informations.Count * SEGMENT_HEIGHT) + (DEFAULT_HEIGHT * 2);
        }

        private void FillInformations()
        {
            informations.Insert((int)InformationIndexs.TITLE, new Information(InformationName.TITLE, split.RunTitle, split.RunsCompleted + "/" + split.AttemptsCount, (int)InformationIndexs.TITLE, true, true));
            informations.Insert((int)InformationIndexs.GOAL, new Information(InformationName.GOAL, "Goal: " + split.RunGoal, null,(int)InformationIndexs.GOAL, true, true));
            informations.Insert((int)InformationIndexs.PREVIOUS_SEGMENT, new Information(InformationName.PREVIOUS_SEGMENT, "Previous segment: ", "-", (int)InformationIndexs.PREVIOUS_SEGMENT, true, false));
            informations.Insert((int)InformationIndexs.POSSIBLE_TIMESAVE, new Information(InformationName.POSSIBLE_TIME_SAVE, "Possible time save: ", "-", (int)InformationIndexs.POSSIBLE_TIMESAVE, true, false));
            informations.Insert((int)InformationIndexs.PREDICTED_TIME, new Information(InformationName.PREDICTED_TIME, "Predicted time: ", "-", (int)InformationIndexs.PREDICTED_TIME, true, false));
            informations.Insert((int)InformationIndexs.SUM_OF_BEST, new Information(InformationName.SUM_OF_BEST, "Sum of best: ", "-", (int)InformationIndexs.SUM_OF_BEST, true, false));
            splitY_start = AboveInformationCount() * SEGMENT_HEIGHT;
        }

        private int AboveInformationCount()
        {
            int aboveNumber = 0;
            foreach (Information information in informations)
            {
                if (information.Above)
                {
                    aboveNumber++;
                }
            }
            return aboveNumber;
        }

        /// <summary>
        /// Used to draw the clock.
        /// </summary>
        /// <param name="graphics">The graphics.</param>
        private void DrawWatch(Graphics graphics)
        {
            string timeString;
            timeString = (displayMode == DisplayMode.SEGMENTS && split.RunStatus == RunStatus.DONE) ? runTimeOnCompletionPause.ToString("hh\\:mm\\:ss\\.ff") : watch.Elapsed.ToString("hh\\:mm\\:ss\\.ff");
            graphics.FillRectangle(new SolidBrush(Settings.Default.TimerBackgroundColor), watchRectangle);
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
            TextRenderer.DrawText(graphics, timeString, Settings.Default.TimerFont, watchRectangle, watchColor, flags);
        }

        /// <summary>
        /// Draw the list of segments.
        /// </summary>
        /// <param name="graphics"></param>
        private void DrawSegments(Graphics graphics)
        {
            string segmentName;
            string segmentSplitTime;
            string runDeltaString = "";
            
            TextFormatFlags nameFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordEllipsis;
            TextFormatFlags splitTimeFlags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | 
                TextFormatFlags.WordBreak;
            TextFormatFlags runDeltaFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordBreak;

            Rectangle segmentNameRectangle;
            Rectangle segmentSplitTimeRectangle;
            Color rectangleColor = Settings.Default.SplitsBackgroundColor;

            for (int i = 0; i < segmentsRectangles.Count; ++i)
            {
                rectangleColor = (i == split.LiveIndex) ? Settings.Default.CurrentSegmentColor : Settings.Default.SplitsBackgroundColor;
                segmentName = split.Segments.ElementAt(i).SegmentName;
                segmentSplitTime = (split.Segments.ElementAt(i).SplitTime == 0) ? "-" : FaceSplitUtils.TimeFormat(split.Segments.ElementAt(i).SplitTime);
                segmentSplitTime = FaceSplitUtils.CutDecimals(segmentSplitTime, 2);
                runDeltaString = GetRunDeltaString(i);
                //if (i == this.split.LiveIndex && (runDeltaString.IndexOf("+") == -1 && runDeltaString.IndexOf("-") == -1))
                //{
                //    runDeltaString = "";
                //}
                runDeltaString = FaceSplitUtils.CutDecimals(runDeltaString, 2);
                segmentNameRectangle = segmentsRectangles.ElementAt(i);
                segmentNameRectangle.Width /= 2;
                segmentSplitTimeRectangle = segmentsRectangles.ElementAt(i);
                segmentSplitTimeRectangle.Width /= 2;
                segmentSplitTimeRectangle.X = segmentNameRectangle.Width;
                graphics.FillRectangle(new SolidBrush(rectangleColor), segmentsRectangles.ElementAt(i));
                TextRenderer.DrawText(graphics, segmentName, Settings.Default.SplitNamesFont,
                    segmentNameRectangle, Settings.Default.SplitNamesColor, nameFlags);
                TextRenderer.DrawText(graphics, segmentSplitTime, Settings.Default.SplitTimesFont,
                    segmentSplitTimeRectangle, Settings.Default.SplitTimesColor, splitTimeFlags);
                if(!string.IsNullOrEmpty(runDeltaString.Trim()))
                {
                    TextRenderer.DrawText(graphics, runDeltaString, Settings.Default.SplitDeltasFont,
                    segmentSplitTimeRectangle, split.GetSegmentColor(i), runDeltaFlags);
                }
            }
        }

        /// <summary>
        /// Fetch the run delta for each split with the index.
        /// Returns it into a string.
        /// </summary>
        /// <param name="index">The index of the split.</param>
        /// <returns>The run delta into a string.</returns>
        private string GetRunDeltaString(int index)
        {
            bool lostTime;
            double runDelta;
            double timeElapsed = (Math.Truncate(segmentWatch.Elapsed.TotalSeconds * 100) / 100) + timeElapsedSinceSplit;
            string runDeltaString = "";
            if (index < split.LiveIndex)
            {
                //Done mean we are after the last split but we still have the possiblity of going back.
                if ((split.RunStatus == RunStatus.ON_GOING || split.RunStatus == RunStatus.DONE) && !split.FirstSplit() && split.SegmentHasRunDelta(index))
                {
                    runDelta = split.GetRunDelta(index);
                    lostTime = (runDelta > 0);
                    runDeltaString = FaceSplitUtils.TimeFormat(Math.Abs(runDelta));
                    if (lostTime)
                    {
                        runDeltaString = runDeltaString.Insert(0, "+");
                    }
                    else
                    {
                        runDeltaString = runDeltaString.Insert(0, "-");
                    }
                }
            }
            else if (index == split.LiveIndex && split.SegmentHasRunDelta(index))
            {
                runDelta = split.GetLiveRunDelta(Math.Truncate(watch.Elapsed.TotalSeconds * 100) / 100);
                lostTime = (runDelta > 0);
                runDeltaString = FaceSplitUtils.TimeFormat(Math.Abs(runDelta));
                if (lostTime)
                {
                    runDeltaString = runDeltaString.Insert(0, "+");
                    if ((index == 0) || (index > 0 && runDelta > split.GetRunDelta(index - 1)))
                    {
                        split.SetCurrentSegmentColor(Settings.Default.SplitDeltasBehindLosingColor);
                    }
                    else
                    {
                        split.SetCurrentSegmentColor(Settings.Default.SplitDeltasBehindSavingColor);
                    }
                    watchColor = Settings.Default.TimerBehindColor;
                }
                else if ((index > 0 && runDelta > split.GetRunDelta(index - 1)))
                {
                    runDeltaString = runDeltaString.Insert(0, "-");
                    split.SetCurrentSegmentColor(Settings.Default.SplitDeltasAheadLosingColor);
                }
                else if (split.CurrentSegmentHasLiveDelta(timeElapsed))
                {
                    runDeltaString = runDeltaString.Insert(0, "-");
                    split.SetCurrentSegmentColor(Settings.Default.SplitDeltasAheadSavingColor);
                }
                else
                {
                    runDeltaString = "";
                    watchColor = Settings.Default.TimerRunningColor;
                }
            }
            return runDeltaString;
        }

        /// <summary>
        /// Draw the list of informations. Such as run title, run goal and previous segment.
        /// </summary>
        /// <param name="graphics"></param>
        private void DrawInformations(Graphics graphics)
        {
            int aboveDrawn = 0;
            int belowDrawn = 0;
            for (int i = 0; i < informations.Count; i++)
            {
                if (informations.ElementAt(i).Above)
                {
                    Rectangle informationRectangle = new Rectangle(0, aboveDrawn * SEGMENT_HEIGHT, DEFAULT_WIDTH, SEGMENT_HEIGHT);
                    graphics.FillRectangle(new SolidBrush(informations.ElementAt(i).BackgroundColor), informationRectangle);
                    TextRenderer.DrawText(graphics, informations.ElementAt(i).PrimaryText, informations.ElementAt(i).PrimaryTextFont,
                        informationRectangle, informations.ElementAt(i).PrimaryTextColor, informations.ElementAt(i).PrimaryTextFlags);
                    if (informations.ElementAt(i).SecondaryText != null)
                    {
                        TextRenderer.DrawText(graphics, informations.ElementAt(i).SecondaryText, informations.ElementAt(i).SecondaryTextFont,
                            informationRectangle, informations.ElementAt(i).SecondaryTextColor, informations.ElementAt(i).SecondaryTextFlags);
                    }
                    aboveDrawn++;
                }
                else
                {
                    Rectangle informationRectangle = new Rectangle(0, (watchRectangle.Y + (watchRectangle.Height * 2)) + (belowDrawn * SEGMENT_HEIGHT), DEFAULT_WIDTH, SEGMENT_HEIGHT);
                    graphics.FillRectangle(new SolidBrush(informations.ElementAt(i).BackgroundColor), informationRectangle);
                    TextRenderer.DrawText(graphics, informations.ElementAt(i).PrimaryText, informations.ElementAt(i).PrimaryTextFont,
                        informationRectangle, informations.ElementAt(i).PrimaryTextColor, informations.ElementAt(i).PrimaryTextFlags);
                    if (informations.ElementAt(i).SecondaryText != null)
                    {
                        TextRenderer.DrawText(graphics, informations.ElementAt(i).SecondaryText, informations.ElementAt(i).SecondaryTextFont,
                            informationRectangle, informations.ElementAt(i).SecondaryTextColor, informations.ElementAt(i).SecondaryTextFlags);
                    }
                    belowDrawn++;
                }
            }
        }

        private void FillEmptySegmentTimer(Graphics graphics)
        {
            Rectangle emptySegmentTimerRectangle = new Rectangle(0, watchRectangle.Y + watchRectangle.Height, DEFAULT_WIDTH, DEFAULT_HEIGHT);
            graphics.FillRectangle(new SolidBrush(Settings.Default.SegmentTimerBackgroundColor), emptySegmentTimerRectangle);
        }

        private void DrawSegmentTimer(Graphics graphics)
        {
            TextFormatFlags segmentTimeFlags = TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.WordEllipsis;
            TextFormatFlags segmentBestTimeFlags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordEllipsis;

            Rectangle segmentTimeRectangle = new Rectangle(0, watchRectangle.Y + watchRectangle.Height, DEFAULT_WIDTH / 2, DEFAULT_HEIGHT / 2);
            Rectangle segmentBestimeRectangle = new Rectangle(0, segmentTimeRectangle.Y + segmentTimeRectangle.Height, DEFAULT_WIDTH / 2, DEFAULT_HEIGHT / 2);
            Rectangle segmentTimerRectangle = new Rectangle(DEFAULT_WIDTH / 2, watchRectangle.Y + watchRectangle.Height, DEFAULT_WIDTH / 2, DEFAULT_HEIGHT);

            graphics.FillRectangle(new SolidBrush(Settings.Default.SegmentTimerBackgroundColor), segmentTimeRectangle);
            graphics.FillRectangle(new SolidBrush(Settings.Default.SegmentTimerBackgroundColor), segmentBestimeRectangle);
            graphics.FillRectangle(new SolidBrush(Settings.Default.SegmentTimerBackgroundColor), segmentTimerRectangle);

            string segmentTime = (split.RunStatus == RunStatus.ON_GOING) ? FaceSplitUtils.TimeFormat(split.CurrentSegment.BackupSegmentTime) : FaceSplitUtils.TimeFormat(split.Segments.Last().BackupSegmentTime);
            string segmentBestTime = (split.RunStatus == RunStatus.ON_GOING) ? FaceSplitUtils.TimeFormat(split.CurrentSegment.BackupBestSegmentTime) : FaceSplitUtils.TimeFormat(split.Segments.Last().BackupBestSegmentTime);
            string segmentTimerString;
            segmentTimerString = (split.RunStatus == RunStatus.DONE) ? segmentTimeOnCompletionPause.ToString()
                : FaceSplitUtils.TimeFormat((Math.Truncate(segmentWatch.Elapsed.TotalSeconds * 100) / 100) + timeElapsedSinceSplit);

            TextRenderer.DrawText(graphics, "PB: " + segmentTime, Settings.Default.SegmentTimerPBFont,
                segmentTimeRectangle, Settings.Default.SegmentTimerPBColor, segmentTimeFlags);

            TextRenderer.DrawText(graphics, "BEST: " + segmentBestTime, Settings.Default.SegmentTimerBestFont,
                segmentBestimeRectangle, Settings.Default.SegmentTimerBestColor, segmentBestTimeFlags);

            TextRenderer.DrawText(graphics, segmentTimerString, Settings.Default.SegmentTimerFont,
                segmentTimerRectangle, segmentWatchColor);
        }

        /// <summary>
        /// For moving the window without a border.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FaceSplit_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void KeyPressed(object sender, KeyEventArgs e)
        {
            if (!globalHotkeysActive)
            {
                switch (e.KeyData)
                {
                    case Keys.Space:
                        KeyboardSplit();
                        break;
                    case Keys.Multiply:
                        KeyboardReset();
                        break;
                    case Keys.Subtract:
                        KeyboardUnsplit();
                        break;
                    case Keys.Divide:
                        KeyboardSkip();
                        break;
                    case Keys.Decimal:
                        KeyboardPause();
                        break;
                }
            }
        }

        private void KeyboardSplit()
        {
            if (displayMode == DisplayMode.TIMER_ONLY)
            {
                if (watch.IsRunning)
                {
                    StopTimer();
                }
                else
                {
                    StartTimer();
                }
            }
            else
            {
                DoSplit();
            }
        }

        private void KeyboardReset()
        {
            ResetTimer();
            if (displayMode == DisplayMode.SEGMENTS)
            {
                split.ResetRun();
                ResetSegmentTimer();
            }
            timeElapsedSinceSplit = 0;
        }

        private void KeyboardUnsplit()
        {
            if (displayMode == DisplayMode.SEGMENTS)
            {
                segmentWatchColor = Settings.Default.SegmentTimerRunningColor;
                if (split.RunStatus == RunStatus.DONE)
                {
                    split.ResumeRun();
                    StartTimer();
                }
                if (split.LiveIndex > 0)
                {
                    Segment lastSegment = split.Segments.ElementAt(split.LiveIndex - 1);
                    timeElapsedSinceSplit += split.Segments.ElementAt(split.LiveIndex - 1).SegmentTime;
                }
                split.UnSplit();
            }
        }

        private void KeyboardSkip()
        {
            if (displayMode == DisplayMode.SEGMENTS && split.RunStatus == RunStatus.ON_GOING && !split.CurrentSplitIsLastSplit())
            {
                segmentWatchColor = Settings.Default.SegmentTimerRunningColor;
                split.SkipSegment((Math.Truncate(segmentWatch.Elapsed.TotalSeconds * 100) / 100));
                segmentWatch.Restart();
            }
        }

        private void KeyboardPause()
        {
            if (split != null && split.RunStatus == RunStatus.ON_GOING)
            {
                if (segmentWatch.IsRunning)
                {
                    segmentWatch.Stop();
                }
                else
                {
                    segmentWatch.Start();
                }
            }
            if (watch.IsRunning)
            {
                StopTimer();
            }
            else
            {
                StartTimer();
            } 
        }

        /// <summary>
        /// When you are in Split mode and you press your split button.
        /// </summary>
        private void DoSplit()
        {
            segmentWatchColor = Settings.Default.SegmentTimerRunningColor;
            if (split.RunStatus == RunStatus.STOPPED)
            {
                StartTimer();
                StartSegmentTimer();
                split.StartRun();
            }
            else if(split.RunStatus == RunStatus.ON_GOING && watch.IsRunning)
            {
                double splitTime = Math.Truncate(watch.Elapsed.TotalSeconds * 100) / 100;
                double segmentTime = (Math.Truncate(segmentWatch.Elapsed.TotalSeconds * 100) / 100) + timeElapsedSinceSplit;
                if (!split.CurrentSplitIsLastSplit())
                {
                    split.DoSplit(splitTime, segmentTime);
                }
                else
                {
                    split.DoSplit(splitTime, segmentTime);
                    split.CompleteRun();
                    runTimeOnCompletionPause = watch.Elapsed;
                    segmentTimeOnCompletionPause = segmentTime;
                    watchColor = Settings.Default.TimerPausedColor;
                    segmentWatchColor = Settings.Default.SegmentTimerPausedColor;
                }
                segmentWatch.Restart();
            }
            else if (split.RunStatus == RunStatus.DONE)
            {
                split.SaveRun();
                ResetTimer();
                ResetSegmentTimer();
            }
            timeElapsedSinceSplit = 0;
        }

        /// <summary>
        /// Update the data that is shown by each information.
        /// </summary>
        public void UpdateInformationsData()
        {
            informations[(int)InformationIndexs.TITLE].SecondaryText = split.RunsCompleted + "/" + split.AttemptsCount;
            informations[(int)InformationIndexs.PREVIOUS_SEGMENT].SecondaryText = GetPreviousSegmentDeltaString();
            informations[(int)InformationIndexs.POSSIBLE_TIMESAVE].SecondaryText = GetPossibleTimeSave();
            informations[(int)InformationIndexs.PREDICTED_TIME].SecondaryText = GetPredictedTime();
            informations[(int)InformationIndexs.SUM_OF_BEST].SecondaryText = GetSOB();
        }

        private string GetPreviousSegmentDeltaString()
        {
            string segmentDeltaString;
            double segmentDelta;
            bool lostTime;
            bool bestSegment = false;
            double timeElapsed = (Math.Truncate(segmentWatch.Elapsed.TotalSeconds * 100) / 100) + timeElapsedSinceSplit;
            if (split.LiveIndex > 0)
            {
                bestSegment = split.PreviousSegmentIsBestSegment();
            }
            if (split.CurrentSegmentHasLiveDelta(timeElapsed))
            {
                informations[(int)InformationIndexs.PREVIOUS_SEGMENT].PrimaryText = "Live segment: ";
                segmentDelta = split.GetLiveSegmentDelta(timeElapsed);
                segmentDeltaString = FaceSplitUtils.TimeFormat(Math.Abs(segmentDelta));
                lostTime = (segmentDelta > 0);
                if (lostTime)
                {
                    informations[(int)InformationIndexs.PREVIOUS_SEGMENT].SecondaryTextColor = Settings.Default.PreviousSegmentDeltaLostColor;
                    segmentDeltaString = segmentDeltaString.Insert(0, "+");
                    segmentWatchColor = Settings.Default.SegmentTimerLosingTimeColor;
                }
                else
                {
                    informations[(int)InformationIndexs.PREVIOUS_SEGMENT].SecondaryTextColor = Settings.Default.PreviousSegmentDeltaSavedColor;
                    segmentDeltaString = segmentDeltaString.Insert(0, "-");
                }
            }
            else if (split.PreviousSegmentHasSegmentDelta())
            {
                informations[(int)InformationIndexs.PREVIOUS_SEGMENT].PrimaryText = "Previous segment: ";
                segmentDelta = split.GetPreviousSegmentDelta();
                segmentDeltaString = FaceSplitUtils.TimeFormat(Math.Abs(segmentDelta));
                lostTime = (segmentDelta > 0);
                if (bestSegment)
                {
                    informations[(int)InformationIndexs.PREVIOUS_SEGMENT].SecondaryTextColor = Settings.Default.PreviousSegmentDeltaBestSegmentColor;
                    segmentDeltaString = segmentDeltaString.Insert(0, "-");
                }
                else
                {
                    if (lostTime)
                    {
                        informations[(int)InformationIndexs.PREVIOUS_SEGMENT].SecondaryTextColor = Settings.Default.PreviousSegmentDeltaLostColor;
                        segmentDeltaString = segmentDeltaString.Insert(0, "+");
                    }
                    else
                    {
                        informations[(int)InformationIndexs.PREVIOUS_SEGMENT].SecondaryTextColor = Settings.Default.PreviousSegmentDeltaSavedColor;
                        segmentDeltaString = segmentDeltaString.Insert(0, "-");
                    }
                }
                split.SetPreviousSegmentColor(bestSegment, lostTime);
            }
            else
            {
                informations[(int)InformationIndexs.PREVIOUS_SEGMENT].PrimaryText = "Previous segment: ";
                informations[(int)InformationIndexs.PREVIOUS_SEGMENT].SecondaryTextColor = Settings.Default.PreviousSegmentDeltaNoDeltaColor;
                segmentDeltaString = "-";
            }
            segmentDeltaString = FaceSplitUtils.CutDecimals(segmentDeltaString, 2);
            return segmentDeltaString;
        }

        public string GetPossibleTimeSave()
        {
            if (split.SegmentHasPossibleTimeSave())
            {
                double possibleTimeSave = split.GetPossibleTimeSave();
                string possibleTimeSaveString = FaceSplitUtils.TimeFormat(possibleTimeSave);
                possibleTimeSaveString = FaceSplitUtils.CutDecimals(possibleTimeSaveString, 2);
                return possibleTimeSaveString;
            }
            return "-";
        }

        public string GetPredictedTime()
        {
            double predictedTime = split.GetPredictedTime();
            string predictedTimeString = "-";
            if (predictedTime != 0.0)
            {
                predictedTimeString = FaceSplitUtils.TimeFormat(predictedTime);
            }
            predictedTimeString = FaceSplitUtils.CutDecimals(predictedTimeString, 2);
            return predictedTimeString;
        }

        public string GetSOB()
        {
            double sob = split.GetSOB();
            string sobString = "-";
            if (sob != 0.0)
            {
                sobString = FaceSplitUtils.TimeFormat(sob);
            }
            sobString = FaceSplitUtils.CutDecimals(sobString, 2);
            return sobString;
        }

        /// <summary>
        /// Start the timer and set the color of the watch.
        /// </summary>
        private void StartTimer()
        {
            watch.Start();
            watchColor = Settings.Default.TimerRunningColor;
            segmentWatchColor = Settings.Default.SegmentTimerRunningColor;
        }

        /// <summary>
        /// Stop the timer and set the color of the watch.
        /// </summary>
        private void StopTimer()
        {
            watch.Stop();
            watchColor = Settings.Default.TimerPausedColor;
            segmentWatchColor = Settings.Default.SegmentTimerPausedColor;
        }

        /// <summary>
        /// Reset the timer and set the color of the watch.
        /// </summary>
        private void ResetTimer()
        {
            watch.Reset();
            watchColor = Settings.Default.TimerNotRunningColor;
            segmentWatchColor = Settings.Default.SegmentTimerNotRunningColor;
        }

        private void StartSegmentTimer()
        {
            segmentWatch.Start();
        }

        private void StopSegmentTimer()
        {
            segmentWatch.Stop();
        }

        private void ResetSegmentTimer()
        {
            segmentWatch.Reset();
        }
    }
}
