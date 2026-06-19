using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HyperNote.Controls
{
    public partial class ImageViewerControl : UserControl
    {
        public event EventHandler? ZoomChanged;
        public event EventHandler? IsDirtyChanged;

        public double ZoomFactor => ImageScale.ScaleX;
        public string ImageFormat { get; private set; } = "Unknown";
        public int ImageWidth { get; private set; }
        public int ImageHeight { get; private set; }
        public bool IsDirty { get; private set; }

        private Point _dragStart;
        private double _startHOffset;
        private double _startVOffset;
        private bool _isDragging;
        private bool _isFitMode = true;
        private GridLength _lastMetadataWidth = new GridLength(300);
        private ImageMetadata? _currentMetadata;

        private class EditState
        {
            public BitmapSource Bitmap { get; }
            public double Brightness { get; }
            public double Contrast { get; }

            public EditState(BitmapSource bitmap, double brightness, double contrast)
            {
                Bitmap = bitmap;
                Brightness = brightness;
                Contrast = contrast;
            }
        }

        // Image Editing State
        private BitmapSource? _currentBitmap;
        private BitmapSource? _baseBitmapForSliders;
        private readonly Stack<EditState> _undoStack = new();
        private readonly Stack<EditState> _redoStack = new();
        private double _lastCommittedBrightness;
        private double _lastCommittedContrast;
        private bool _isUpdatingSliders;
        private DispatcherTimer? _sliderTimer;
        private bool _isUpdatingResizeText;

        // Crop selection dragging variables
        private enum DragMode { None, Move, ResizeTop, ResizeBottom, ResizeLeft, ResizeRight, ResizeTopLeft, ResizeTopRight, ResizeBottomLeft, ResizeBottomRight }
        private DragMode _cropDragMode = DragMode.None;
        private Point _cropDragStart;
        private Rect _cropBoxStart;

        public ImageViewerControl()
        {
            InitializeComponent();
            
            this.Loaded += ImageViewerControl_Loaded;
            ImgScrollViewer.SizeChanged += ImgScrollViewer_SizeChanged;

            // Hook up slider LostMouseCapture to commit brightness/contrast changes
            SldBrightness.LostMouseCapture += Slider_LostMouseCapture;
            SldContrast.LostMouseCapture += Slider_LostMouseCapture;

            // Hook up double-click to reset sliders to 0
            SldBrightness.PreviewMouseDoubleClick += Slider_MouseDoubleClick;
            SldContrast.PreviewMouseDoubleClick += Slider_MouseDoubleClick;
        }

        public void Open(string path)
        {
            try
            {
                var metadata = ReadMetadata(path);
                _currentMetadata = metadata;

                var bitmap = new BitmapImage();
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }
                
                bitmap.Freeze();
                _currentBitmap = bitmap;
                DisplayImage.Source = _currentBitmap;

                ImageFormat = metadata.Format;
                ImageWidth = metadata.Width;
                ImageHeight = metadata.Height;

                // Set initial values for resize boxes
                _isUpdatingResizeText = true;
                TxtResizeWidth.Text = ImageWidth.ToString();
                TxtResizeHeight.Text = ImageHeight.ToString();
                _isUpdatingResizeText = false;

                PopulateMetadataPanel(metadata);

                _undoStack.Clear();
                _redoStack.Clear();
                _lastCommittedBrightness = 0;
                _lastCommittedContrast = 0;
                _isUpdatingSliders = true;
                SldBrightness.Value = 0;
                SldContrast.Value = 0;
                TxtBrightnessVal.Text = "0";
                TxtContrastVal.Text = "0";
                _isUpdatingSliders = false;
                UpdateUndoRedoButtons();

                IsDirty = false;
                IsDirtyChanged?.Invoke(this, EventArgs.Empty);

                _isFitMode = true;
                ResetTransforms();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load image: {ex.Message}", "Error Loading Image", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetTransforms()
        {
            ImageScale.ScaleX = 1.0;
            ImageScale.ScaleY = 1.0;
            ImageRotate.Angle = 0;
            UpdateZoomUI();
        }

        public void ResetZoom()
        {
            SetZoom(1.0);
        }

        private void SetZoom(double scale)
        {
            _isFitMode = false;
            double oldScale = ImageScale.ScaleX;
            double newScale = Math.Clamp(scale, 0.05, 50.0);

            if (newScale != oldScale)
            {
                double viewW = ImgScrollViewer.ViewportWidth;
                double viewH = ImgScrollViewer.ViewportHeight;
                if (viewW <= 0) viewW = ImgScrollViewer.ActualWidth;
                if (viewH <= 0) viewH = ImgScrollViewer.ActualHeight;

                double contentX = viewW / 2 + ImgScrollViewer.HorizontalOffset;
                double contentY = viewH / 2 + ImgScrollViewer.VerticalOffset;

                double ratio = newScale / oldScale;

                ImageScale.ScaleX = newScale;
                ImageScale.ScaleY = newScale;

                ImageGrid.UpdateLayout();

                ImgScrollViewer.ScrollToHorizontalOffset(contentX * ratio - viewW / 2);
                ImgScrollViewer.ScrollToVerticalOffset(contentY * ratio - viewH / 2);

                UpdateZoomUI();
            }
        }

        private void FitImageToViewport()
        {
            if (DisplayImage.Source == null) return;

            bool isRotated90 = ((int)Math.Round(ImageRotate.Angle) / 90) % 2 != 0;
            double imgW = isRotated90 ? DisplayImage.Source.Height : DisplayImage.Source.Width;
            double imgH = isRotated90 ? DisplayImage.Source.Width : DisplayImage.Source.Height;

            double viewW = ImgScrollViewer.ViewportWidth;
            double viewH = ImgScrollViewer.ViewportHeight;

            if (viewW <= 0) viewW = ImgScrollViewer.ActualWidth;
            if (viewH <= 0) viewH = ImgScrollViewer.ActualHeight;

            if (viewW > 0 && viewH > 0 && imgW > 0 && imgH > 0)
            {
                double scaleX = viewW / imgW;
                double scaleY = viewH / imgH;
                double scale = Math.Min(scaleX, scaleY);

                double fitScale = Math.Min(scale, 1.0);
                if (fitScale <= 0) fitScale = 1.0;

                ImageScale.ScaleX = fitScale;
                ImageScale.ScaleY = fitScale;
                
                ImageGrid.UpdateLayout();
                UpdateZoomUI();
            }
        }

        private void ToggleFitOrActual()
        {
            if (_isFitMode)
            {
                SetZoom(1.0);
            }
            else
            {
                _isFitMode = true;
                FitImageToViewport();
            }
        }

        private void UpdateZoomUI()
        {
            int pct = (int)Math.Round(ImageScale.ScaleX * 100);
            TxtZoomPercent.Text = $"{pct}%";
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ToggleMetadataPanel(bool show)
        {
            if (show)
            {
                MetadataColumn.Width = _lastMetadataWidth;
                MetadataSplitter.Visibility = Visibility.Visible;
                MetadataPanel.Visibility = Visibility.Visible;
            }
            else
            {
                _lastMetadataWidth = MetadataColumn.Width;
                MetadataColumn.Width = new GridLength(0);
                MetadataSplitter.Visibility = Visibility.Collapsed;
                MetadataPanel.Visibility = Visibility.Collapsed;
            }
        }

        // =====================================================================
        //  Undo / Redo state management
        // =====================================================================

        private void PushState(BitmapSource newBitmap, bool resetSliders = true)
        {
            if (_currentBitmap != null)
            {
                _undoStack.Push(new EditState(_currentBitmap, _lastCommittedBrightness, _lastCommittedContrast));
            }
            _redoStack.Clear();
            _currentBitmap = newBitmap;
            DisplayImage.Source = _currentBitmap;

            if (resetSliders)
            {
                _isUpdatingSliders = true;
                SldBrightness.Value = 0;
                SldContrast.Value = 0;
                TxtBrightnessVal.Text = "0";
                TxtContrastVal.Text = "0";
                _lastCommittedBrightness = 0;
                _lastCommittedContrast = 0;
                _isUpdatingSliders = false;
                _baseBitmapForSliders = null;
            }

            ImageWidth = _currentBitmap.PixelWidth;
            ImageHeight = _currentBitmap.PixelHeight;

            UpdateUndoRedoButtons();
            
            if (_currentMetadata != null)
            {
                _currentMetadata.Width = ImageWidth;
                _currentMetadata.Height = ImageHeight;
                PopulateMetadataPanel(_currentMetadata);
            }

            _isUpdatingResizeText = true;
            TxtResizeWidth.Text = ImageWidth.ToString();
            TxtResizeHeight.Text = ImageHeight.ToString();
            _isUpdatingResizeText = false;

            if (!IsDirty)
            {
                IsDirty = true;
                IsDirtyChanged?.Invoke(this, EventArgs.Empty);
            }

            // Adjust zoom settings for the new dimensions
            if (_isFitMode)
            {
                FitImageToViewport();
            }
        }

        private void UpdateUndoRedoButtons()
        {
            BtnUndo.IsEnabled = _undoStack.Count > 0;
            BtnRedo.IsEnabled = _redoStack.Count > 0;
        }

        public void Undo()
        {
            if (_undoStack.Count > 0 && _currentBitmap != null)
            {
                _redoStack.Push(new EditState(_currentBitmap, _lastCommittedBrightness, _lastCommittedContrast));
                
                var state = _undoStack.Pop();
                _currentBitmap = state.Bitmap;
                DisplayImage.Source = _currentBitmap;

                _isUpdatingSliders = true;
                SldBrightness.Value = state.Brightness;
                SldContrast.Value = state.Contrast;
                TxtBrightnessVal.Text = Math.Round(state.Brightness).ToString();
                TxtContrastVal.Text = Math.Round(state.Contrast).ToString();
                _lastCommittedBrightness = state.Brightness;
                _lastCommittedContrast = state.Contrast;
                _isUpdatingSliders = false;
                _baseBitmapForSliders = null;

                ImageWidth = _currentBitmap.PixelWidth;
                ImageHeight = _currentBitmap.PixelHeight;

                UpdateUndoRedoButtons();
                
                if (_currentMetadata != null)
                {
                    _currentMetadata.Width = ImageWidth;
                    _currentMetadata.Height = ImageHeight;
                    PopulateMetadataPanel(_currentMetadata);
                }

                _isUpdatingResizeText = true;
                TxtResizeWidth.Text = ImageWidth.ToString();
                TxtResizeHeight.Text = ImageHeight.ToString();
                _isUpdatingResizeText = false;

                if (_undoStack.Count == 0)
                {
                    IsDirty = false;
                    IsDirtyChanged?.Invoke(this, EventArgs.Empty);
                }

                if (_isFitMode) FitImageToViewport();
                ZoomChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Redo()
        {
            if (_redoStack.Count > 0 && _currentBitmap != null)
            {
                _undoStack.Push(new EditState(_currentBitmap, _lastCommittedBrightness, _lastCommittedContrast));
                
                var state = _redoStack.Pop();
                _currentBitmap = state.Bitmap;
                DisplayImage.Source = _currentBitmap;

                _isUpdatingSliders = true;
                SldBrightness.Value = state.Brightness;
                SldContrast.Value = state.Contrast;
                TxtBrightnessVal.Text = Math.Round(state.Brightness).ToString();
                TxtContrastVal.Text = Math.Round(state.Contrast).ToString();
                _lastCommittedBrightness = state.Brightness;
                _lastCommittedContrast = state.Contrast;
                _isUpdatingSliders = false;
                _baseBitmapForSliders = null;

                ImageWidth = _currentBitmap.PixelWidth;
                ImageHeight = _currentBitmap.PixelHeight;

                UpdateUndoRedoButtons();
                
                if (_currentMetadata != null)
                {
                    _currentMetadata.Width = ImageWidth;
                    _currentMetadata.Height = ImageHeight;
                    PopulateMetadataPanel(_currentMetadata);
                }

                _isUpdatingResizeText = true;
                TxtResizeWidth.Text = ImageWidth.ToString();
                TxtResizeHeight.Text = ImageHeight.ToString();
                _isUpdatingResizeText = false;

                if (!IsDirty)
                {
                    IsDirty = true;
                    IsDirtyChanged?.Invoke(this, EventArgs.Empty);
                }

                if (_isFitMode) FitImageToViewport();
                ZoomChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            
            // Wire Ctrl+Z and Ctrl+Y keys to editor operations
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.Z)
                {
                    if (BtnUndo.IsEnabled)
                    {
                        Undo();
                        e.Handled = true;
                    }
                }
                else if (e.Key == Key.Y)
                {
                    if (BtnRedo.IsEnabled)
                    {
                        Redo();
                        e.Handled = true;
                    }
                }
            }
        }

        // =====================================================================
        //  Save Logic
        // =====================================================================

        public void Save(string savePath)
        {
            if (_currentBitmap == null) return;

            // Commit visual layout rotation into the exported pixel data
            BitmapSource outputSource = _currentBitmap;
            if (ImageRotate.Angle != 0)
            {
                outputSource = new TransformedBitmap(_currentBitmap, new RotateTransform(ImageRotate.Angle));
            }

            string ext = Path.GetExtension(savePath).ToLowerInvariant();
            BitmapEncoder encoder = ext switch
            {
                ".png" => new PngBitmapEncoder(),
                ".gif" => new GifBitmapEncoder(),
                ".bmp" => new BmpBitmapEncoder(),
                ".tif" or ".tiff" => new TiffBitmapEncoder(),
                ".wdp" or ".hdp" => new WmpBitmapEncoder(),
                _ => new JpegBitmapEncoder { QualityLevel = 90 }
            };

            encoder.Frames.Add(BitmapFrame.Create(outputSource));

            string tempPath = Path.Combine(Path.GetDirectoryName(savePath) ?? Path.GetTempPath(), "~temp_" + Path.GetFileName(savePath));
            try
            {
                // Save atomically via temporary file
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(stream);
                }

                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }
                File.Move(tempPath, savePath);

                // Commit transforms
                _currentBitmap = outputSource;
                DisplayImage.Source = _currentBitmap;
                ImageRotate.Angle = 0;

                IsDirty = false;
                IsDirtyChanged?.Invoke(this, EventArgs.Empty);

                // Clear history since file matches disk state
                _undoStack.Clear();
                _redoStack.Clear();
                UpdateUndoRedoButtons();

                // Reload metadata properties from saved file
                var newMetadata = ReadMetadata(savePath);
                _currentMetadata = newMetadata;
                PopulateMetadataPanel(newMetadata);
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
                throw new Exception("Error writing image file: " + ex.Message, ex);
            }
        }

        // =====================================================================
        //  Edit Event Handlers
        // =====================================================================

        private void BtnToggleEdit_Click(object sender, RoutedEventArgs e)
        {
            EditToolbar.Visibility = BtnToggleEdit.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (BtnToggleEdit.IsChecked != true && BtnCropMode.IsChecked == true)
            {
                ExitCropMode(false);
            }
        }

        // --- CROP HANDLERS ---
        private void BtnCropMode_Click(object sender, RoutedEventArgs e)
        {
            if (BtnCropMode.IsChecked == true)
            {
                EnterCropMode();
            }
            else
            {
                ExitCropMode(false);
            }
        }

        private void EnterCropMode()
        {
            BtnCropMode.IsChecked = true;
            CropCanvas.Visibility = Visibility.Visible;
            CropBox.Visibility = Visibility.Visible;
            BtnApplyCrop.Visibility = Visibility.Visible;
            BtnCancelCrop.Visibility = Visibility.Visible;
            
            // Focus image grid layout and initialize the centered CropBox
            ImageGrid.UpdateLayout();
            InitializeCropBox();
        }

        private void ExitCropMode(bool apply)
        {
            if (apply && _currentBitmap != null)
            {
                double canvasW = CropCanvas.ActualWidth;
                double canvasH = CropCanvas.ActualHeight;

                if (canvasW > 0 && canvasH > 0)
                {
                    double x = Canvas.GetLeft(CropBox);
                    double y = Canvas.GetTop(CropBox);
                    double w = CropBox.Width;
                    double h = CropBox.Height;

                    // Map layout coordinates back to raw pixels (supports DPI and layouts)
                    double ratioX = (double)_currentBitmap.PixelWidth / canvasW;
                    double ratioY = (double)_currentBitmap.PixelHeight / canvasH;

                    int pixelX = (int)Math.Max(0, Math.Round(x * ratioX));
                    int pixelY = (int)Math.Max(0, Math.Round(y * ratioY));
                    int pixelW = (int)Math.Min(_currentBitmap.PixelWidth - pixelX, Math.Round(w * ratioX));
                    int pixelH = (int)Math.Min(_currentBitmap.PixelHeight - pixelY, Math.Round(h * ratioY));

                    if (pixelW > 5 && pixelH > 5)
                    {
                        var cropped = new CroppedBitmap(_currentBitmap, new Int32Rect(pixelX, pixelY, pixelW, pixelH));
                        PushState(cropped);
                    }
                }
            }

            BtnCropMode.IsChecked = false;
            CropCanvas.Visibility = Visibility.Collapsed;
            CropBox.Visibility = Visibility.Collapsed;
            BtnApplyCrop.Visibility = Visibility.Collapsed;
            BtnCancelCrop.Visibility = Visibility.Collapsed;
        }

        private void BtnApplyCrop_Click(object sender, RoutedEventArgs e)
        {
            ExitCropMode(true);
        }

        private void BtnCancelCrop_Click(object sender, RoutedEventArgs e)
        {
            ExitCropMode(false);
        }

        private void CbAspectRatio_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BtnCropMode.IsChecked == true)
            {
                InitializeCropBox();
            }
        }

        // --- RESIZE HANDLERS ---
        private void TxtResizeWidth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingResizeText || ChkLockAspect.IsChecked != true || _currentBitmap == null) return;
            if (int.TryParse(TxtResizeWidth.Text, out int newW) && newW > 0 && ImageWidth > 0)
            {
                _isUpdatingResizeText = true;
                double ratio = (double)ImageHeight / ImageWidth;
                int newH = (int)Math.Round(newW * ratio);
                TxtResizeHeight.Text = newH.ToString();
                _isUpdatingResizeText = false;
            }
        }

        private void TxtResizeHeight_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingResizeText || ChkLockAspect.IsChecked != true || _currentBitmap == null) return;
            if (int.TryParse(TxtResizeHeight.Text, out int newH) && newH > 0 && ImageHeight > 0)
            {
                _isUpdatingResizeText = true;
                double ratio = (double)ImageWidth / ImageHeight;
                int newW = (int)Math.Round(newH * ratio);
                TxtResizeWidth.Text = newW.ToString();
                _isUpdatingResizeText = false;
            }
        }

        private void BtnApplyResize_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBitmap == null) return;
            if (int.TryParse(TxtResizeWidth.Text, out int newW) && newW > 0 &&
                int.TryParse(TxtResizeHeight.Text, out int newH) && newH > 0)
            {
                if (newW == ImageWidth && newH == ImageHeight) return;

                double scaleX = (double)newW / _currentBitmap.PixelWidth;
                double scaleY = (double)newH / _currentBitmap.PixelHeight;
                var resized = new TransformedBitmap(_currentBitmap, new ScaleTransform(scaleX, scaleY));
                PushState(resized);
            }
        }

        // --- FLIP HANDLERS ---
        private void BtnFlipHorizontal_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBitmap == null) return;
            var flipped = new TransformedBitmap(_currentBitmap, new ScaleTransform(-1, 1));
            PushState(flipped);
        }

        private void BtnFlipVertical_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBitmap == null) return;
            var flipped = new TransformedBitmap(_currentBitmap, new ScaleTransform(1, -1));
            PushState(flipped);
        }

        // --- COLOR ADJUSTMENTS ---
        private void SldBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingSliders) return;
            if (TxtBrightnessVal != null)
            {
                TxtBrightnessVal.Text = Math.Round(SldBrightness.Value).ToString();
            }
            TriggerSliderAdjustment();
        }

        private void SldContrast_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingSliders) return;
            if (TxtContrastVal != null)
            {
                TxtContrastVal.Text = Math.Round(SldContrast.Value).ToString();
            }
            TriggerSliderAdjustment();
        }

        private void TriggerSliderAdjustment()
        {
            if (_currentBitmap == null) return;

            if (_baseBitmapForSliders == null)
            {
                _baseBitmapForSliders = _currentBitmap;
            }

            _sliderTimer?.Stop();
            _sliderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _sliderTimer.Tick += (s, ev) =>
            {
                _sliderTimer.Stop();
                if (_baseBitmapForSliders != null)
                {
                    double b = SldBrightness.Value - _lastCommittedBrightness;
                    double c = SldContrast.Value - _lastCommittedContrast;
                    
                    var adjusted = ApplyBrightnessContrast(_baseBitmapForSliders, b, c);
                    DisplayImage.Source = adjusted;
                }
            };
            _sliderTimer.Start();
        }

        private void CommitCurrentSliderAdjustments()
        {
            _sliderTimer?.Stop();
            if (_baseBitmapForSliders != null && _currentBitmap != null)
            {
                double b = SldBrightness.Value - _lastCommittedBrightness;
                double c = SldContrast.Value - _lastCommittedContrast;

                var adjusted = ApplyBrightnessContrast(_baseBitmapForSliders, b, c);
                
                if (adjusted != _baseBitmapForSliders)
                {
                    _undoStack.Push(new EditState(_baseBitmapForSliders, _lastCommittedBrightness, _lastCommittedContrast));
                    _redoStack.Clear();
                    
                    _currentBitmap = adjusted;
                    DisplayImage.Source = _currentBitmap;

                    _lastCommittedBrightness = SldBrightness.Value;
                    _lastCommittedContrast = SldContrast.Value;

                    UpdateUndoRedoButtons();

                    if (!IsDirty)
                    {
                        IsDirty = true;
                        IsDirtyChanged?.Invoke(this, EventArgs.Empty);
                    }
                }

                _baseBitmapForSliders = null;
            }
        }

        private void Slider_LostMouseCapture(object sender, MouseEventArgs e)
        {
            CommitCurrentSliderAdjustments();
        }

        private void Slider_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider)
            {
                if (slider.Value != 0)
                {
                    if (_baseBitmapForSliders == null)
                    {
                        _baseBitmapForSliders = _currentBitmap;
                    }

                    slider.Value = 0;
                    CommitCurrentSliderAdjustments();
                    e.Handled = true;
                }
            }
        }

        private void BtnGrayscale_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBitmap == null) return;
            try
            {
                var grayscale = new FormatConvertedBitmap(_currentBitmap, PixelFormats.Gray8, null, 0);
                PushState(grayscale);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to convert to grayscale: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            Undo();
        }

        private void BtnRedo_Click(object sender, RoutedEventArgs e)
        {
            Redo();
        }

        // =====================================================================
        //  Crop Mouse Operations
        // =====================================================================

        private void CropCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                Point pos = e.GetPosition(CropCanvas);
                _cropDragMode = GetDragModeAtPosition(pos);
                _cropDragStart = pos;
                _cropBoxStart = new Rect(Canvas.GetLeft(CropBox), Canvas.GetTop(CropBox), CropBox.Width, CropBox.Height);
                CropCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        private void CropCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point pos = e.GetPosition(CropCanvas);
            double canvasW = CropCanvas.ActualWidth;
            double canvasH = CropCanvas.ActualHeight;

            if (_cropDragMode == DragMode.None)
            {
                DragMode hoverMode = GetDragModeAtPosition(pos);
                CropCanvas.Cursor = GetCursorForMode(hoverMode);
                return;
            }

            double dx = pos.X - _cropDragStart.X;
            double dy = pos.Y - _cropDragStart.Y;

            double x = _cropBoxStart.X;
            double y = _cropBoxStart.Y;
            double w = _cropBoxStart.Width;
            double h = _cropBoxStart.Height;

            double minSize = 24.0;

            switch (_cropDragMode)
            {
                case DragMode.Move:
                    x += dx;
                    y += dy;
                    x = Math.Clamp(x, 0, canvasW - w);
                    y = Math.Clamp(y, 0, canvasH - h);
                    break;

                case DragMode.ResizeLeft:
                    w -= dx;
                    if (w >= minSize) x += dx;
                    else { w = minSize; x = _cropBoxStart.Right - minSize; }
                    break;

                case DragMode.ResizeRight:
                    w += dx;
                    w = Math.Clamp(w, minSize, canvasW - x);
                    break;

                case DragMode.ResizeTop:
                    h -= dy;
                    if (h >= minSize) y += dy;
                    else { h = minSize; y = _cropBoxStart.Bottom - minSize; }
                    break;

                case DragMode.ResizeBottom:
                    h += dy;
                    h = Math.Clamp(h, minSize, canvasH - y);
                    break;

                case DragMode.ResizeTopLeft:
                    w -= dx;
                    h -= dy;
                    if (w >= minSize) x += dx; else { w = minSize; x = _cropBoxStart.Right - minSize; }
                    if (h >= minSize) y += dy; else { h = minSize; y = _cropBoxStart.Bottom - minSize; }
                    break;

                case DragMode.ResizeTopRight:
                    w += dx;
                    h -= dy;
                    w = Math.Clamp(w, minSize, canvasW - x);
                    if (h >= minSize) y += dy; else { h = minSize; y = _cropBoxStart.Bottom - minSize; }
                    break;

                case DragMode.ResizeBottomLeft:
                    w -= dx;
                    h += dy;
                    if (w >= minSize) x += dx; else { w = minSize; x = _cropBoxStart.Right - minSize; }
                    h = Math.Clamp(h, minSize, canvasH - y);
                    break;

                case DragMode.ResizeBottomRight:
                    w += dx;
                    h += dy;
                    w = Math.Clamp(w, minSize, canvasW - x);
                    h = Math.Clamp(h, minSize, canvasH - y);
                    break;
            }

            // Enforce aspect ratio preset if any
            double ratio = GetSelectedAspectRatio();
            if (ratio > 0 && _cropDragMode != DragMode.Move)
            {
                if (_cropDragMode == DragMode.ResizeLeft || _cropDragMode == DragMode.ResizeRight ||
                    _cropDragMode == DragMode.ResizeTopLeft || _cropDragMode == DragMode.ResizeTopRight ||
                    _cropDragMode == DragMode.ResizeBottomLeft || _cropDragMode == DragMode.ResizeBottomRight)
                {
                    h = w / ratio;
                }
                else
                {
                    w = h * ratio;
                }

                if (_cropDragMode == DragMode.ResizeLeft || _cropDragMode == DragMode.ResizeTopLeft || _cropDragMode == DragMode.ResizeBottomLeft)
                {
                    x = _cropBoxStart.Right - w;
                }
                if (_cropDragMode == DragMode.ResizeTop || _cropDragMode == DragMode.ResizeTopLeft || _cropDragMode == DragMode.ResizeTopRight)
                {
                    y = _cropBoxStart.Bottom - h;
                }

                if (x < 0) { x = 0; w = _cropBoxStart.Right; h = w / ratio; }
                if (y < 0) { y = 0; h = _cropBoxStart.Bottom; w = h * ratio; }
                if (x + w > canvasW) { w = canvasW - x; h = w / ratio; }
                if (y + h > canvasH) { h = canvasH - y; w = h * ratio; }
            }
            else if (ratio <= 0 && _cropDragMode != DragMode.Move)
            {
                // Freeform clamping to keep selection within bounds
                if (x < 0) { w += x; x = 0; }
                if (y < 0) { h += y; y = 0; }
                if (x + w > canvasW) { w = canvasW - x; }
                if (y + h > canvasH) { h = canvasH - y; }

                if (w < minSize) w = minSize;
                if (h < minSize) h = minSize;
            }

            Canvas.SetLeft(CropBox, x);
            Canvas.SetTop(CropBox, y);
            CropBox.Width = w;
            CropBox.Height = h;

            UpdateCropMask();
            e.Handled = true;
        }

        private void CropCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_cropDragMode != DragMode.None)
            {
                _cropDragMode = DragMode.None;
                CropCanvas.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private DragMode GetDragModeAtPosition(Point pos)
        {
            double x = Canvas.GetLeft(CropBox);
            double y = Canvas.GetTop(CropBox);
            double w = CropBox.Width;
            double h = CropBox.Height;
            double margin = 12.0;

            if (Math.Abs(pos.X - x) < margin && Math.Abs(pos.Y - y) < margin) return DragMode.ResizeTopLeft;
            if (Math.Abs(pos.X - (x + w)) < margin && Math.Abs(pos.Y - y) < margin) return DragMode.ResizeTopRight;
            if (Math.Abs(pos.X - x) < margin && Math.Abs(pos.Y - (y + h)) < margin) return DragMode.ResizeBottomLeft;
            if (Math.Abs(pos.X - (x + w)) < margin && Math.Abs(pos.Y - (y + h)) < margin) return DragMode.ResizeBottomRight;

            if (pos.X >= x && pos.X <= x + w)
            {
                if (Math.Abs(pos.Y - y) < margin) return DragMode.ResizeTop;
                if (Math.Abs(pos.Y - (y + h)) < margin) return DragMode.ResizeBottom;
            }
            if (pos.Y >= y && pos.Y <= y + h)
            {
                if (Math.Abs(pos.X - x) < margin) return DragMode.ResizeLeft;
                if (Math.Abs(pos.X - (x + w)) < margin) return DragMode.ResizeRight;
            }

            if (pos.X > x && pos.X < x + w && pos.Y > y && pos.Y < y + h) return DragMode.Move;

            return DragMode.None;
        }

        private Cursor GetCursorForMode(DragMode mode)
        {
            return mode switch
            {
                DragMode.Move => Cursors.SizeAll,
                DragMode.ResizeLeft or DragMode.ResizeRight => Cursors.SizeWE,
                DragMode.ResizeTop or DragMode.ResizeBottom => Cursors.SizeNS,
                DragMode.ResizeTopLeft or DragMode.ResizeBottomRight => Cursors.SizeNWSE,
                DragMode.ResizeTopRight or DragMode.ResizeBottomLeft => Cursors.SizeNESW,
                _ => Cursors.Arrow
            };
        }

        private double GetSelectedAspectRatio()
        {
            return CbAspectRatio.SelectedIndex switch
            {
                1 => 1.0,           // 1:1 Square
                2 => 16.0 / 9.0,    // 16:9 Wide
                3 => 4.0 / 3.0,     // 4:3 Standard
                _ => 0.0            // Freeform
            };
        }

        private void InitializeCropBox()
        {
            double canvasW = CropCanvas.ActualWidth;
            double canvasH = CropCanvas.ActualHeight;
            if (canvasW <= 0 || canvasH <= 0)
            {
                canvasW = DisplayImage.ActualWidth;
                canvasH = DisplayImage.ActualHeight;
            }

            if (canvasW <= 0 || canvasH <= 0) return;

            double ratio = GetSelectedAspectRatio();
            double w = canvasW * 0.8;
            double h = canvasH * 0.8;

            if (ratio > 0)
            {
                if (w / ratio <= canvasH * 0.8)
                {
                    h = w / ratio;
                }
                else
                {
                    w = h * ratio;
                }
            }

            double x = (canvasW - w) / 2;
            double y = (canvasH - h) / 2;

            Canvas.SetLeft(CropBox, x);
            Canvas.SetTop(CropBox, y);
            CropBox.Width = w;
            CropBox.Height = h;

            UpdateCropMask();
        }

        private void UpdateCropMask()
        {
            double canvasW = CropCanvas.ActualWidth;
            double canvasH = CropCanvas.ActualHeight;
            if (canvasW <= 0 || canvasH <= 0)
            {
                canvasW = DisplayImage.ActualWidth;
                canvasH = DisplayImage.ActualHeight;
            }

            double x = Canvas.GetLeft(CropBox);
            double y = Canvas.GetTop(CropBox);
            double w = CropBox.Width;
            double h = CropBox.Height;

            var outerRect = new RectangleGeometry(new Rect(0, 0, canvasW, canvasH));
            var innerRect = new RectangleGeometry(new Rect(x, y, w, h));
            var maskGeom = new CombinedGeometry(GeometryCombineMode.Exclude, outerRect, innerRect);
            CropMask.Data = maskGeom;
        }

        // =====================================================================
        //  Pixel Processing Logic
        // =====================================================================

        private static BitmapSource ApplyBrightnessContrast(BitmapSource source, double brightnessVal, double contrastVal)
        {
            if (brightnessVal == 0 && contrastVal == 0) return source;

            var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = formatted.PixelWidth;
            int height = formatted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            formatted.CopyPixels(pixels, stride, 0);

            double b = brightnessVal;
            double c = contrastVal;
            double factor = (100.0 + c) / 100.0;
            factor = factor * factor; // Curve shaping for more natural adjustments

            for (int i = 0; i < pixels.Length; i += 4)
            {
                for (int j = 0; j < 3; j++) // R, G, B channels
                {
                    double val = pixels[i + j];
                    
                    val += b;
                    val = 128.0 + (val - 128.0) * factor;
                    
                    if (val < 0) val = 0;
                    else if (val > 255) val = 255;
                    
                    pixels[i + j] = (byte)val;
                }
            }

            return BitmapSource.Create(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        }

        // =====================================================================
        //  WPF Panning and Zooming Events
        // =====================================================================

        private void ImageViewerControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isFitMode) FitImageToViewport();
        }

        private void ImgScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isFitMode) FitImageToViewport();
            if (BtnCropMode.IsChecked == true) InitializeCropBox();
        }

        private void ImgScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                double oldScale = ImageScale.ScaleX;
                double zoomFactor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
                double newScale = oldScale * zoomFactor;

                newScale = Math.Clamp(newScale, 0.05, 50.0);

                if (newScale != oldScale)
                {
                    _isFitMode = false;

                    Point mouseInScrollViewer = e.GetPosition(ImgScrollViewer);
                    double contentX = mouseInScrollViewer.X + ImgScrollViewer.HorizontalOffset;
                    double contentY = mouseInScrollViewer.Y + ImgScrollViewer.VerticalOffset;

                    double ratio = newScale / oldScale;

                    ImageScale.ScaleX = newScale;
                    ImageScale.ScaleY = newScale;

                    ImageGrid.UpdateLayout();

                    ImgScrollViewer.ScrollToHorizontalOffset(contentX * ratio - mouseInScrollViewer.X);
                    ImgScrollViewer.ScrollToVerticalOffset(contentY * ratio - mouseInScrollViewer.Y);

                    UpdateZoomUI();
                }
            }
        }

        private void ImgScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (BtnCropMode.IsChecked == true)
            {
                return;
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.OriginalSource is DependencyObject dep && IsInScrollBar(dep))
                {
                    return;
                }

                if (e.ClickCount == 2)
                {
                    ToggleFitOrActual();
                    e.Handled = true;
                    return;
                }

                _dragStart = e.GetPosition(ImgScrollViewer);
                _startHOffset = ImgScrollViewer.HorizontalOffset;
                _startVOffset = ImgScrollViewer.VerticalOffset;
                _isDragging = true;
                ImgScrollViewer.CaptureMouse();
                ImgScrollViewer.Cursor = Cursors.Hand;
                e.Handled = true;
            }
        }

        private void ImgScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point current = e.GetPosition(ImgScrollViewer);
                double deltaX = current.X - _dragStart.X;
                double deltaY = current.Y - _dragStart.Y;

                ImgScrollViewer.ScrollToHorizontalOffset(_startHOffset - deltaX);
                ImgScrollViewer.ScrollToVerticalOffset(_startVOffset - deltaY);
                e.Handled = true;
            }
        }

        private void ImgScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && _isDragging)
            {
                _isDragging = false;
                ImgScrollViewer.ReleaseMouseCapture();
                ImgScrollViewer.Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        public void ZoomIn()
        {
            SetZoom(ImageScale.ScaleX * 1.2);
        }

        public void ZoomOut()
        {
            SetZoom(ImageScale.ScaleX / 1.2);
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            ZoomOut();
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            ZoomIn();
        }

        private void BtnZoomActual_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(1.0);
        }

        private void BtnZoomFit_Click(object sender, RoutedEventArgs e)
        {
            _isFitMode = true;
            FitImageToViewport();
        }

        private void BtnRotateLeft_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBitmap == null) return;
            try
            {
                var rotated = new TransformedBitmap(_currentBitmap, new RotateTransform(-90));
                PushState(rotated);
                
                if (_isFitMode) FitImageToViewport();
                if (BtnCropMode.IsChecked == true) InitializeCropBox();
                
                ZoomChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to rotate: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRotateRight_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBitmap == null) return;
            try
            {
                var rotated = new TransformedBitmap(_currentBitmap, new RotateTransform(90));
                PushState(rotated);
                
                if (_isFitMode) FitImageToViewport();
                if (BtnCropMode.IsChecked == true) InitializeCropBox();
                
                ZoomChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to rotate: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnToggleMetadata_Click(object sender, RoutedEventArgs e)
        {
            ToggleMetadataPanel(BtnToggleMetadata.IsChecked == true);
        }

        private void BtnCloseMetadata_Click(object sender, RoutedEventArgs e)
        {
            BtnToggleMetadata.IsChecked = false;
            ToggleMetadataPanel(false);
        }

        // =====================================================================
        //  Sidebar Details population
        // =====================================================================

        private void PopulateMetadataPanel(ImageMetadata metadata)
        {
            MetadataStackPanel.Children.Clear();

            var fileItems = new List<(string Key, string Value)>
            {
                ("Name", metadata.FileName),
                ("Folder", Path.GetDirectoryName(metadata.FilePath) ?? ""),
                ("Size", FormatSize(metadata.FileSize)),
                ("Created", metadata.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss")),
                ("Modified", metadata.ModifiedDate.ToString("yyyy-MM-dd HH:mm:ss"))
            };
            AddMetadataGroup("File Details", fileItems);

            var imgItems = new List<(string Key, string Value)>
            {
                ("Format", metadata.Format),
                ("Dimensions", $"{metadata.Width} × {metadata.Height} px"),
                ("Aspect Ratio", GetAspectRatio(metadata.Width, metadata.Height)),
                ("Color Depth", $"{metadata.ColorDepth} bits")
            };
            AddMetadataGroup("Image Properties", imgItems);

            if (metadata.HasExif)
            {
                var exifItems = new List<(string Key, string Value)>();
                if (!string.IsNullOrEmpty(metadata.CameraManufacturer))
                    exifItems.Add(("Manufacturer", metadata.CameraManufacturer));
                if (!string.IsNullOrEmpty(metadata.CameraModel))
                    exifItems.Add(("Model", metadata.CameraModel));
                if (metadata.DateTaken.HasValue)
                    exifItems.Add(("Date Taken", metadata.DateTaken.Value.ToString("yyyy-MM-dd HH:mm:ss")));
                if (!string.IsNullOrEmpty(metadata.ExposureTime))
                    exifItems.Add(("Exposure Time", metadata.ExposureTime));
                if (metadata.Aperture.HasValue)
                    exifItems.Add(("Aperture", $"f/{metadata.Aperture.Value:F1}"));
                if (metadata.IsoSpeed.HasValue)
                    exifItems.Add(("ISO Speed", $"ISO {metadata.IsoSpeed.Value}"));
                if (metadata.FocalLength.HasValue)
                    exifItems.Add(("Focal Length", $"{metadata.FocalLength.Value:F1} mm"));
                if (!string.IsNullOrEmpty(metadata.Software))
                    exifItems.Add(("Software", metadata.Software));

                if (exifItems.Count > 0)
                {
                    AddMetadataGroup("Camera Info", exifItems);
                }
            }

            AddCopyButton(metadata);
        }

        private void AddMetadataGroup(string header, List<(string Key, string Value)> items)
        {
            if (items == null || items.Count == 0) return;

            var headerText = new TextBlock
            {
                Text = header.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 14, 0, 6)
            };
            headerText.SetResourceReference(TextBlock.ForegroundProperty, "Editor.LineNumbers");
            MetadataStackPanel.Children.Add(headerText);

            var groupPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

            foreach (var item in items)
            {
                var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var keyText = new TextBlock
                {
                    Text = item.Key,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Top
                };
                keyText.SetResourceReference(TextBlock.ForegroundProperty, "Editor.LineNumbers");

                var valText = new TextBox
                {
                    Text = item.Value,
                    FontSize = 11,
                    IsReadOnly = true,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    TextWrapping = TextWrapping.Wrap,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0),
                    IsTabStop = false,
                    VerticalAlignment = VerticalAlignment.Top
                };
                valText.SetResourceReference(TextBox.ForegroundProperty, "App.Foreground");

                Grid.SetColumn(keyText, 0);
                Grid.SetColumn(valText, 1);
                grid.Children.Add(keyText);
                grid.Children.Add(valText);

                groupPanel.Children.Add(grid);
            }

            MetadataStackPanel.Children.Add(groupPanel);
        }

        private void AddCopyButton(ImageMetadata metadata)
        {
            var btn = new Button
            {
                Content = "Copy Metadata",
                Margin = new Thickness(0, 16, 0, 16),
                Padding = new Thickness(10, 6, 10, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand
            };
            btn.Click += (s, e) => {
                try
                {
                    string report = FormatMetadataReport(metadata);
                    Clipboard.SetText(report);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to copy metadata: {ex.Message}", "Error Copying", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            MetadataStackPanel.Children.Add(btn);
        }

        // =====================================================================
        //  Metadata helper methods
        // =====================================================================

        private static ImageMetadata ReadMetadata(string filePath)
        {
            var info = new ImageMetadata
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath)
            };

            try
            {
                var fileInfo = new FileInfo(filePath);
                info.FileSize = fileInfo.Length;
                info.CreatedDate = fileInfo.CreationTime;
                info.ModifiedDate = fileInfo.LastWriteTime;

                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];

                    info.Width = frame.PixelWidth;
                    info.Height = frame.PixelHeight;
                    info.Format = FormatFriendlyName(decoder.CodecInfo.FriendlyName);
                    info.ColorDepth = frame.Format.BitsPerPixel;

                    if (frame.Metadata is BitmapMetadata meta)
                    {
                        info.HasExif = true;
                        info.CameraManufacturer = meta.CameraManufacturer?.Trim();
                        info.CameraModel = meta.CameraModel?.Trim();
                        info.DateTaken = TryParseDate(meta.DateTaken);
                        info.Software = meta.ApplicationName?.Trim();

                        info.IsoSpeed = GetQueryValue<ushort>(meta, "/System.Photo.ISOSpeed")
                                        ?? GetQueryValue<ushort>(meta, "/app1/ifd0/exif/{ushort=34855}");

                        info.Aperture = GetQueryValue<double>(meta, "/System.Photo.FNumber")
                                        ?? GetQueryValue<double>(meta, "/app1/ifd0/exif/{ushort=33437}");

                        info.FocalLength = GetQueryValue<double>(meta, "/System.Photo.FocalLength")
                                           ?? GetQueryValue<double>(meta, "/app1/ifd0/exif/{ushort=37386}");

                        var expTimeObj = meta.GetQuery("/System.Photo.ExposureTime")
                                         ?? meta.GetQuery("/app1/ifd0/exif/{ushort=33434}");
                        if (expTimeObj != null)
                        {
                            info.ExposureTime = FormatExposureTime(expTimeObj);
                        }
                    }
                }
            }
            catch
            {
                // Fall back to simple details on file read exceptions
            }

            return info;
        }

        private static string FormatFriendlyName(string friendlyName)
        {
            if (string.IsNullOrEmpty(friendlyName)) return "Unknown";
            if (friendlyName.Contains("PNG", StringComparison.OrdinalIgnoreCase)) return "PNG";
            if (friendlyName.Contains("JPEG", StringComparison.OrdinalIgnoreCase) || friendlyName.Contains("JPG", StringComparison.OrdinalIgnoreCase)) return "JPEG";
            if (friendlyName.Contains("BMP", StringComparison.OrdinalIgnoreCase)) return "BMP";
            if (friendlyName.Contains("GIF", StringComparison.OrdinalIgnoreCase)) return "GIF";
            if (friendlyName.Contains("TIFF", StringComparison.OrdinalIgnoreCase)) return "TIFF";
            if (friendlyName.Contains("ICO", StringComparison.OrdinalIgnoreCase)) return "ICO";
            if (friendlyName.Contains("WDP", StringComparison.OrdinalIgnoreCase) || friendlyName.Contains("Media Photo", StringComparison.OrdinalIgnoreCase)) return "WDP";
            return friendlyName;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
            return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
        }

        private static int FindGcd(int a, int b)
        {
            while (b != 0)
            {
                int temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }

        private static string GetAspectRatio(int w, int h)
        {
            if (w <= 0 || h <= 0) return "Unknown";
            double ratio = (double)w / h;

            int gcd = FindGcd(w, h);
            int num = w / gcd;
            int den = h / gcd;

            if (num > 50 || den > 50)
            {
                double[] targets = { 16.0 / 9.0, 4.0 / 3.0, 3.0 / 2.0, 1.0, 16.0 / 10.0, 21.0 / 9.0, 5.0 / 4.0 };
                string[] labels = { "16:9", "4:3", "3:2", "1:1", "16:10", "21:9", "5:4" };

                for (int i = 0; i < targets.Length; i++)
                {
                    if (Math.Abs(ratio - targets[i]) < 0.015)
                    {
                        return $"{labels[i]} ({ratio:F2})";
                    }
                }
                return $"{ratio:F2}:1";
            }

            return $"{num}:{den} ({ratio:F2})";
        }

        private static string FormatMetadataReport(ImageMetadata metadata)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("--- IMAGE METADATA ---");
            sb.AppendLine($"[FILE DETAILS]");
            sb.AppendLine($"Name: {metadata.FileName}");
            sb.AppendLine($"Path: {metadata.FilePath}");
            sb.AppendLine($"Size: {FormatSize(metadata.FileSize)}");
            sb.AppendLine($"Created: {metadata.CreatedDate:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Modified: {metadata.ModifiedDate:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine($"[IMAGE PROPERTIES]");
            sb.AppendLine($"Format: {metadata.Format}");
            sb.AppendLine($"Dimensions: {metadata.Width} x {metadata.Height}");
            sb.AppendLine($"Aspect Ratio: {GetAspectRatio(metadata.Width, metadata.Height)}");
            sb.AppendLine($"Color Depth: {metadata.ColorDepth} bits");

            if (metadata.HasExif)
            {
                sb.AppendLine();
                sb.AppendLine($"[CAMERA & EXIF DETAILS]");
                if (!string.IsNullOrEmpty(metadata.CameraManufacturer)) sb.AppendLine($"Manufacturer: {metadata.CameraManufacturer}");
                if (!string.IsNullOrEmpty(metadata.CameraModel)) sb.AppendLine($"Model: {metadata.CameraModel}");
                if (metadata.DateTaken.HasValue) sb.AppendLine($"Date Taken: {metadata.DateTaken.Value:yyyy-MM-dd HH:mm:ss}");
                if (!string.IsNullOrEmpty(metadata.ExposureTime)) sb.AppendLine($"Exposure Time: {metadata.ExposureTime}");
                if (metadata.Aperture.HasValue) sb.AppendLine($"Aperture: f/{metadata.Aperture.Value:F1}");
                if (metadata.IsoSpeed.HasValue) sb.AppendLine($"ISO Speed: ISO {metadata.IsoSpeed.Value}");
                if (metadata.FocalLength.HasValue) sb.AppendLine($"Focal Length: {metadata.FocalLength.Value:F1} mm");
                if (!string.IsNullOrEmpty(metadata.Software)) sb.AppendLine($"Software: {metadata.Software}");
            }
            return sb.ToString();
        }

        private static T? GetQueryValue<T>(BitmapMetadata meta, string query) where T : struct
        {
            try
            {
                if (meta.ContainsQuery(query))
                {
                    object value = meta.GetQuery(query);
                    if (value is T typedValue) return typedValue;
                    return (T)Convert.ChangeType(value, typeof(T));
                }
            }
            catch { }
            return null;
        }

        private static string FormatExposureTime(object value)
        {
            if (value is double d)
            {
                if (d >= 1.0) return $"{d:F1}s";
                if (d > 0.0)
                {
                    double reciprocal = 1.0 / d;
                    return $"1/{Math.Round(reciprocal)}s";
                }
                return $"{d}s";
            }
            else if (value is ulong ul)
            {
                uint num = (uint)(ul >> 32);
                uint den = (uint)(ul & 0xFFFFFFFF);
                if (den != 0)
                {
                    if (num >= den) return $"{(double)num / den:F1}s";
                    return $"{num}/{den}s";
                }
            }
            return value.ToString() ?? "";
        }

        private static DateTime? TryParseDate(string? dateStr)
        {
            if (string.IsNullOrEmpty(dateStr)) return null;
            if (DateTime.TryParse(dateStr, out DateTime dt)) return dt;
            return null;
        }

        private static bool IsInScrollBar(DependencyObject? obj)
        {
            while (obj != null)
            {
                if (obj is System.Windows.Controls.Primitives.ScrollBar)
                    return true;
                obj = VisualTreeHelper.GetParent(obj);
            }
            return false;
        }
    }
}
