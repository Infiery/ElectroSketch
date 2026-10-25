using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ElectroSketch.Core;
using Microsoft.Win32;

namespace ElectroSketch;

public partial class MainWindow : Window
{
    private sealed record ComponentPaletteItem(ComponentType Type, string Name, string Category, string Detail, Brush Tint);

    private static readonly (string Name, string Hex)[] WireColors =
    [
        ("Black · USB GND", "#20262D"), ("Red · USB VBUS", "#E53935"),
        ("Green · USB D+", "#27AE60"), ("White · USB D−", "#F2F2F2"),
        ("Orange · illustrative", "#F4A52C"), ("Blue · illustrative", "#3285D5"),
        ("Yellow · illustrative", "#F2C94C"), ("Purple · illustrative", "#8E44AD"),
        ("Brown · AC live", "#8B5A2B")
    ];
    private CircuitDocument _document = SampleCircuitFactory.Create();
    private CircuitSnapshot? _lastSnapshot;
    private string? _currentPath;
    private bool _isDirty;
    private bool _updatingUi;
    private bool _updatingAnnotationColor;
    private int _placementIndex;
    private bool _isPanning;
    private bool _pendingBackgroundPan;
    private bool _componentsOpen = true;
    private bool _meterOpen = true;
    private readonly List<string> _undoHistory = [];
    private readonly List<string> _redoHistory = [];
    private bool _restoringHistory;
    private Point _panStart;
    private double _panStartHorizontal;
    private double _panStartVertical;
    private GlobalNotesData _notes = new();
    private readonly ObservableCollection<NoteImageItem> _noteImages = [];
    private readonly ObservableCollection<ComponentPaletteItem> _allPaletteItems = [];
    private readonly ObservableCollection<ComponentPaletteItem> _visiblePaletteItems = [];
    private ComponentPaletteItem? _paletteDragItem;
    private Point _paletteDragStart;
    private bool _loadingNotes;
    private string? _activeSafetyKey;
    private readonly string? _startupPath = Environment.GetCommandLineArgs()
        .Skip(1)
        .FirstOrDefault(path => path.EndsWith(".ets", StringComparison.OrdinalIgnoreCase) && File.Exists(path));

    public MainWindow()
    {
        InitializeComponent();
        BuildComponentPalette();
        FitWindowToWorkArea();
        CircuitSurface.Document = _document;
        CircuitSurface.DocumentChanged += (_, _) => OnCircuitChanged();
        CircuitSurface.ProbeChanged += (_, _) =>
        {
            // A continuity tone belongs to the active pair of probe contacts.  Clearing
            // both probes (including with right-click) must silence it immediately.
            if (CircuitSurface.Document.Meter.RedProbe is null && CircuitSurface.Document.Meter.BlackProbe is null)
                SafetySounds.StopContinuity();
            _isDirty = true;
            RefreshAll();
            CheckSafetyNow();
        };
        CircuitSurface.StatusChanged += (_, message) =>
        {
            if (!CircuitSurface.SafetyLocked) StatusText.Text = message;
        };
        CircuitSurface.WorkspaceExpandedBefore += (dx, dy) =>
        {
            if (CanvasScale is null || CanvasScrollViewer is null) return;
            var scale = CanvasScale.ScaleX;
            var shiftX = dx * scale;
            var shiftY = dy * scale;
            if (_isPanning)
            {
                _panStartHorizontal += shiftX;
                _panStartVertical += shiftY;
            }
            CanvasScrollViewer.UpdateLayout();
            CanvasScrollViewer.ScrollToHorizontalOffset(CanvasScrollViewer.HorizontalOffset + shiftX);
            CanvasScrollViewer.ScrollToVerticalOffset(CanvasScrollViewer.VerticalOffset + shiftY);
        };
        CircuitSurface.SelectionChanged += (_, _) => { RefreshDocumentText(); UpdateConnectionGuide(); SyncAnnotationColorToSelection(); };
        CircuitSurface.TextRequested += CircuitSurface_TextRequested;
        CircuitSurface.AnnotationEditRequested += CircuitSurface_AnnotationEditRequested;
        SetWireColor(1);
        ResetHistory();
        LoadGlobalNotes();
        Loaded += (_, _) =>
        {
            if (_startupPath is not null)
                LoadDocument(_startupPath);
            else
                RefreshAll();
            CanvasScrollViewer.ScrollToHorizontalOffset(0);
            CanvasScrollViewer.ScrollToVerticalOffset(0);
        };
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        _document = new CircuitDocument { Name = "Untitled topology" };
        _currentPath = null;
        _isDirty = false;
        CircuitSurface.Document = _document;
        ResetHistory();
        RefreshAll();
        CheckSafetyNow();
        StatusText.Text = "New empty topology. Add components from the left.";
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog
        {
            Title = "Open ElectroSketch topology",
            Filter = "ElectroSketch topology (*.ets)|*.ets|JSON files (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        LoadDocument(dialog.FileName);
    }

    private void LoadDocument(string fileName)
    {
        try
        {
            if (new FileInfo(fileName).Length > DocumentValidator.MaxFileBytes)
                throw new InvalidDataException("This topology file is too large to open safely.");
            var json = File.ReadAllText(fileName);
            var loaded = JsonSerializer.Deserialize<CircuitDocument>(json, DocumentJson.Options);
            if (loaded is null) throw new InvalidDataException("The file did not contain a topology.");
            _document = DocumentValidator.Normalize(loaded);
            _document.Name = Path.GetFileNameWithoutExtension(fileName);
            _currentPath = fileName;
            _isDirty = false;
            CircuitSurface.Document = _document;
            ResetHistory();
            RefreshAll();
            CheckSafetyNow();
            StatusText.Text = $"Opened {Path.GetFileName(fileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"ElectroSketch could not open that file.\n\n{ex.Message}", "Open topology", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveDocument();

    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveDocument(saveAs: true);

    private bool SaveDocument(bool saveAs = false)
    {
        var path = _currentPath;
        if (path is null || saveAs)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save ElectroSketch topology",
                Filter = "ElectroSketch topology (*.ets)|*.ets",
                FileName = SanitizeFileName(_document.Name) + ".ets",
                AddExtension = true,
                DefaultExt = ".ets"
            };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        var previousName = _document.Name;
        _document.Name = Path.GetFileNameWithoutExtension(path);
        try
        {
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_document, DocumentJson.Options));
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            _currentPath = path;
            _isDirty = false;
            StatusText.Text = $"Saved {Path.GetFileName(path)}";
            RefreshDocumentText();
            return true;
        }
        catch (Exception ex)
        {
            _document.Name = previousName;
            MessageBox.Show(this, $"ElectroSketch could not save the file.\n\n{ex.Message}", "Save topology", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    // Legacy palette event targets remain wired in the hidden template so older XAML
    // entries continue to work if that template is restored for migration.
    private void AddComponent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string typeText } && Enum.TryParse<ComponentType>(typeText, out var type)) AddComponent(type);
    }
    private void AddUsb2Connector_Click(object sender, RoutedEventArgs e) => AddSelectedConnector(Usb2ConnectorCombo);
    private void AddUsb3Connector_Click(object sender, RoutedEventArgs e) => AddSelectedConnector(Usb3ConnectorCombo);
    private void AddLightningConnector_Click(object sender, RoutedEventArgs e) => AddSelectedConnector(LightningConnectorCombo);
    private void AddAdapter_Click(object sender, RoutedEventArgs e) => AddSelectedConnector(AdapterCombo);
    private void AddEthernet_Click(object sender, RoutedEventArgs e) => AddSelectedConnector(EthernetCombo);
    private void AddReadyCable_Click(object sender, RoutedEventArgs e) => AddSelectedConnector(ReadyCableCombo);
    private void AddDevice_Click(object sender, RoutedEventArgs e) => AddSelectedConnector(DeviceCombo);

    private void AddSelectedConnector(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem { Tag: string typeText } && Enum.TryParse<ComponentType>(typeText, out var type)) AddComponent(type);
    }

    private void BuildComponentPalette()
    {
        foreach (var type in Enum.GetValues<ComponentType>())
        {
            var category = FormatCategory(ComponentCatalog.GetCategory(type));
            var name = ConnectorCatalog.TryGet(type, out var connector)
                ? connector.Title
                : ComponentCatalog.Create(type, 0, 0).Name;
            _allPaletteItems.Add(new ComponentPaletteItem(type, name, category, PaletteDetail(type), PaletteTint(ComponentCatalog.GetCategory(type))));
        }
        PaletteCategoryCombo.ItemsSource = new[] { "All categories" }.Concat(_allPaletteItems.Select(item => item.Category).Distinct().OrderBy(value => value)).ToList();
        PaletteCategoryCombo.SelectedIndex = 0;
        ComponentList.ItemsSource = _visiblePaletteItems;
        ApplyPaletteFilter();
    }

    private static string PaletteDetail(ComponentType type)
    {
        if (ComponentCatalog.IsReadyCable(type))
            return $"{ComponentCatalog.CableSignals(type).Length} modeled paths · {ReadyCableCatalog.Ends(type).Left} to {ReadyCableCatalog.Ends(type).Right}";
        if (ConnectorCatalog.TryGet(type, out var connector))
            return type is ComponentType.LightningPlug or ComponentType.LightningReceptacle
                ? "8 contacts · simplified VBUS/GND power groups"
                : $"{connector.Pins.Count} individual contacts · {connector.Subtitle}";
        if (ComponentCatalog.IsPowerAdapter(type))
        {
            var item = ComponentCatalog.Create(type, 0, 0);
            return $"{item.Properties.GetValueOrDefault("OutputVoltage", "5")} V DC · up to {item.Properties.GetValueOrDefault("MaxCurrent", "2")} A";
        }
        return type switch
        {
            ComponentType.Breadboard => "12 connected rows on each side · four power rails",
            ComponentType.ArduinoHeader => "5 V, GND and digital pins D2–D7",
            ComponentType.WallPlug => "Türkiye · 230 V AC RMS · 50 Hz",
            ComponentType.PowerStrip3 => "Three switched sockets · L, N and PE",
            ComponentType.PowerStrip6 => "Six switched sockets · L, N and PE",
            ComponentType.Resistor => "220 Ω default · editable properties",
            ComponentType.Led => "Anode and cathode · 1.8 V default",
            ComponentType.Speaker4Ohm2W => "4 Ω · 2 W passive speaker",
            ComponentType.MihawkDriver => "32 Ω headphone driver model",
            _ when ComponentCatalog.IsRechargeableDevice(type) => "Rechargeable · auto-charges when powered",
            _ when ComponentCatalog.IsEthernetCable(type) => "8 conductors · pin-to-pin",
            _ => "Placeable connection or circuit object"
        };
    }

    private static Brush PaletteTint(ComponentCategory category)
    {
        var color = category switch
        {
            ComponentCategory.Controllers => Color.FromRgb(226, 243, 249),
            ComponentCategory.PowerSources or ComponentCategory.PowerAdapters => Color.FromRgb(250, 239, 218),
            ComponentCategory.Prototyping => Color.FromRgb(246, 241, 224),
            ComponentCategory.Indicators or ComponentCategory.PassiveComponents => Color.FromRgb(252, 234, 230),
            ComponentCategory.Usb2Connectors or ComponentCategory.UsbSuperSpeedConnectors => Color.FromRgb(226, 241, 249),
            ComponentCategory.LightningConnectors => Color.FromRgb(238, 240, 244),
            ComponentCategory.PowerDistribution => Color.FromRgb(240, 233, 248),
            ComponentCategory.Audio => Color.FromRgb(244, 234, 250),
            ComponentCategory.Ethernet => Color.FromRgb(229, 246, 234),
            _ => Color.FromRgb(236, 240, 243)
        };
        return new SolidColorBrush(color);
    }

    private void ApplyPaletteFilter()
    {
        if (_visiblePaletteItems is null) return;
        var category = PaletteCategoryCombo.SelectedItem as string ?? "All categories";
        var query = PaletteSearchBox.Text.Trim();
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = _allPaletteItems.Where(item =>
            (category == "All categories" || item.Category == category) &&
            (terms.Length == 0 || terms.All(term =>
            {
                var searchable = $"{item.Name} {item.Category} {item.Detail} {item.Type}";
                return searchable.Contains(term, StringComparison.OrdinalIgnoreCase);
            }))).ToList();
        _visiblePaletteItems.Clear();
        foreach (var item in matches) _visiblePaletteItems.Add(item);
        if (ComponentList.SelectedItem is ComponentPaletteItem selected && !matches.Contains(selected)) ComponentList.SelectedItem = null;
        PaletteCountText.Text = $"{matches.Count} items";
    }

    private void PaletteSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyPaletteFilter();
    private void PaletteCategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyPaletteFilter();

    private void ComponentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComponentList.SelectedItem is ComponentPaletteItem item)
        {
            PaletteDetailText.Text = $"{item.Category}\n{item.Detail}";
            PaletteAddButton.IsEnabled = true;
        }
        else
        {
            PaletteDetailText.Text = "Choose an item to see its contacts and connection behavior.";
            PaletteAddButton.IsEnabled = false;
        }
    }

    private void PaletteAddSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ComponentList.SelectedItem is ComponentPaletteItem item) AddComponent(item.Type);
    }

    private void ComponentList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => PaletteAddSelected_Click(sender, e);

    private void ComponentList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _paletteDragStart = e.GetPosition(ComponentList);
        var source = e.OriginalSource as DependencyObject;
        _paletteDragItem = source is null
            ? null
            : ItemsControl.ContainerFromElement(ComponentList, source) is ListBoxItem container
                ? container.DataContext as ComponentPaletteItem
                : null;
    }

    private void ComponentList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_paletteDragItem is null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(ComponentList);
        if (Math.Abs(point.X - _paletteDragStart.X) < 5 && Math.Abs(point.Y - _paletteDragStart.Y) < 5) return;
        var item = _paletteDragItem;
        _paletteDragItem = null;
        DragDrop.DoDragDrop(ComponentList, new DataObject(typeof(ComponentType), item.Type), DragDropEffects.Copy);
    }

    private void Canvas_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ComponentType)) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Canvas_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ComponentType)) is not ComponentType type) return;
        CircuitSurface.AddComponentAt(type, CircuitSurface.ViewToWorld(e.GetPosition(CircuitSurface)));
        SelectModeButton.IsChecked = true;
        e.Handled = true;
    }

    private void AddComponent(ComponentType type)
    {
        var scale = Math.Max(0.1, CanvasScale.ScaleX);
        var viewCenter = new Point((CanvasScrollViewer.HorizontalOffset + CanvasScrollViewer.ViewportWidth / 2) / scale,
                                   (CanvasScrollViewer.VerticalOffset + CanvasScrollViewer.ViewportHeight / 2) / scale);
        var center = CircuitSurface.ViewToWorld(viewCenter);
        center.Offset((_placementIndex % 5) * 24, (_placementIndex % 4) * 24);
        _placementIndex++;
        SelectModeButton.IsChecked = true;
        CircuitSurface.AddComponentAt(type, center);
    }

    private void OnCircuitChanged()
    {
        _isDirty = true;
        if (!_restoringHistory) RecordHistory();
        RefreshAll();
        CheckSafetyNow();
    }

    private void ResetHistory()
    {
        _undoHistory.Clear();
        _redoHistory.Clear();
        _undoHistory.Add(JsonSerializer.Serialize(_document, DocumentJson.Options));
    }

    private void RecordHistory()
    {
        var snapshot = JsonSerializer.Serialize(_document, DocumentJson.Options);
        if (_undoHistory.Count > 0 && _undoHistory[^1] == snapshot) return;
        _undoHistory.Add(snapshot);
        if (_undoHistory.Count > 6) _undoHistory.RemoveAt(0);
        _redoHistory.Clear();
    }

    private void Undo()
    {
        if (_undoHistory.Count < 2) { StatusText.Text = "Nothing left to undo."; return; }
        _redoHistory.Add(_undoHistory[^1]);
        if (_redoHistory.Count > 5) _redoHistory.RemoveAt(0);
        _undoHistory.RemoveAt(_undoHistory.Count - 1);
        RestoreHistory(_undoHistory[^1], "Undo");
    }

    private void Redo()
    {
        if (_redoHistory.Count == 0) { StatusText.Text = "Nothing left to redo."; return; }
        var snapshot = _redoHistory[^1];
        _redoHistory.RemoveAt(_redoHistory.Count - 1);
        _undoHistory.Add(snapshot);
        RestoreHistory(snapshot, "Redo");
    }

    private void RestoreHistory(string snapshot, string action)
    {
        var restored = JsonSerializer.Deserialize<CircuitDocument>(snapshot, DocumentJson.Options);
        if (restored is null) return;
        _restoringHistory = true;
        _document = DocumentValidator.Normalize(restored);
        CircuitSurface.Document = _document;
        _restoringHistory = false;
        _isDirty = true;
        RefreshAll();
        CheckSafetyNow();
        StatusText.Text = $"{action} complete.";
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not RadioButton { Tag: string modeText } || !Enum.TryParse<EditorMode>(modeText, out var mode)) return;
        CircuitSurface.CancelAction();
        CircuitSurface.Mode = mode;
        WireOptionsPanel.Visibility = mode == EditorMode.Wire ? Visibility.Visible : Visibility.Collapsed;
        if (mode == EditorMode.Probe)
        {
            CircuitSurface.PlaceRedProbe = false;
            StatusText.Text = "Meter probes: click a connection point for the black probe, then one for red.";
        }
        else if (mode == EditorMode.Wire)
            StatusText.Text = "Wire mode: click two points. Keys 1–9 select wire colors.";
        else if (mode == EditorMode.AnnotationText)
            StatusText.Text = "Text tool: click the playground and enter your label.";
        else if (mode == EditorMode.DrawLine)
            StatusText.Text = "Line tool: drag from the start to the end of a line.";
        else if (mode == EditorMode.DrawArrow)
            StatusText.Text = "Arrow tool: drag from the start toward the arrowhead.";
        else
            StatusText.Text = "Select mode: drag components or click a wire. Press Delete to remove the selection.";
    }

    private void CircuitSurface_TextRequested(object? sender, Point point)
    {
        var dialog = new TextEntryDialog { Owner = this };
        if (dialog.ShowDialog() == true)
            CircuitSurface.AddTextAnnotation(point, dialog.AnnotationText);
    }

    private void CircuitSurface_AnnotationEditRequested(object? sender, AnnotationModel annotation)
    {
        var dialog = new TextEntryDialog(annotation.Text, editing: true) { Owner = this };
        if (dialog.ShowDialog() == true)
            CircuitSurface.UpdateTextAnnotation(annotation, dialog.AnnotationText);
    }

    private void AnnotationColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingAnnotationColor && CircuitSurface is not null && AnnotationColorComboBox.SelectedItem is ComboBoxItem { Tag: string color })
            CircuitSurface.SetSelectedAnnotationColor(color);
    }

    private void SyncAnnotationColorToSelection()
    {
        if (CircuitSurface.SelectedAnnotation is not { Type: AnnotationType.Text } annotation) return;
        _updatingAnnotationColor = true;
        foreach (var item in AnnotationColorComboBox.Items.OfType<ComboBoxItem>())
            if (Equals(item.Tag, annotation.Color)) { AnnotationColorComboBox.SelectedItem = item; break; }
        _updatingAnnotationColor = false;
    }

    private void WireColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string numberText } && int.TryParse(numberText, out var number)) SetWireColor(number);
    }

    private void SetWireColor(int number)
    {
        if (number < 1 || number > WireColors.Length || CircuitSurface is null) return;
        var color = WireColors[number - 1];
        CircuitSurface.WireColor = color.Hex;
        if (CurrentWireColorSwatch is not null) CurrentWireColorSwatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color.Hex));
        if (CurrentWireColorText is not null) CurrentWireColorText.Text = $"{color.Name} · key {number}";
        if (IsLoaded && CircuitSurface.Mode == EditorMode.Wire) StatusText.Text = $"Wire color {number}: {color.Name}";
    }

    private void MeterModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _updatingUi || MeterModeCombo.SelectedItem is not ComboBoxItem { Tag: string modeText } || !Enum.TryParse<MeterMode>(modeText, out var mode)) return;
        _document.Meter.Mode = mode;
        SafetySounds.StopContinuity();
        _isDirty = true;
        PopulateMeterRanges();
        RefreshMeter();
        CheckSafetyNow();
    }

    private void MeterProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _updatingUi || MeterProfileCombo.SelectedItem is not ComboBoxItem { Tag: string profileText } || !Enum.TryParse<MeterProfile>(profileText, out var profile)) return;
        _document.Meter.Profile = profile;
        _document.Meter.Range = profile == MeterProfile.Mas830L ? "20" : "Auto";
        PopulateMeterRanges();
        _isDirty = true;
        RefreshMeter();
        CheckSafetyNow();
    }

    private void MeterRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _updatingUi || MeterRangeCombo.SelectedItem is not ComboBoxItem { Tag: string range }) return;
        _document.Meter.Range = range;
        _isDirty = true;
        RefreshMeter();
        CheckSafetyNow();
    }

    private void PopulateMeterRanges()
    {
        if (MeterRangeCombo is null) return;
        _updatingUi = true;
        MeterRangeCombo.Items.Clear();
        if (_document.Meter.Profile == MeterProfile.Mas830L)
        {
            var ranges = _document.Meter.Mode switch
            {
                MeterMode.DcVoltage => new[] { "0.2", "2", "20", "200", "600" },
                MeterMode.AcVoltage => new[] { "200", "600" },
                MeterMode.DcCurrent => new[] { "0.0002", "0.002", "0.02", "0.2", "10" },
                MeterMode.Resistance => new[] { "200", "2000", "20000", "200000", "2000000" },
                _ => Array.Empty<string>()
            };
            foreach (var range in ranges)
                MeterRangeCombo.Items.Add(new ComboBoxItem { Tag = range, Content = range switch { "0.0002" => "200 µA", "0.002" => "2 mA", "0.02" => "20 mA", "0.2" => "200 mA", "10" => "10 A", "2000" => "2 kΩ", "20000" => "20 kΩ", "200000" => "200 kΩ", "2000000" => "2 MΩ", _ => range + (_document.Meter.Mode == MeterMode.Resistance ? " Ω" : " V") } });
            MeterRangeCombo.Visibility = ranges.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            var match = MeterRangeCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, _document.Meter.Range));
            MeterRangeCombo.SelectedItem = match ?? MeterRangeCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();
            if (MeterRangeCombo.SelectedItem is ComboBoxItem selected) _document.Meter.Range = selected.Tag?.ToString() ?? "20";
        }
        else
        {
            MeterRangeCombo.Visibility = Visibility.Collapsed;
            _document.Meter.Range = "Auto";
        }
        _updatingUi = false;
    }

    private void ArmBlackProbe_Click(object sender, RoutedEventArgs e)
    {
        ActivateProbeMode(false);
        StatusText.Text = "Black probe armed — click a round connection point.";
    }

    private void ArmRedProbe_Click(object sender, RoutedEventArgs e)
    {
        ActivateProbeMode(true);
        StatusText.Text = "Red probe armed — click a round connection point.";
    }

    private void ActivateProbeMode(bool red)
    {
        foreach (var item in FindVisualChildren<RadioButton>(this).Where(r => Equals(r.Tag, "Probe"))) item.IsChecked = true;
        CircuitSurface.Mode = EditorMode.Probe;
        CircuitSurface.PlaceRedProbe = red;
    }

    private void ResetSafety_Click(object sender, RoutedEventArgs e)
    {
        CircuitSurface.SafetyLocked = false;
        SafetyBorder.Visibility = Visibility.Collapsed;
        StatusText.Text = "Editing unlocked. The reported electrical risk remains until the topology or meter setup is corrected.";
    }

    private SafetyFinding? GetSafetyFinding()
    {
        var snapshot = _lastSnapshot ??= new CircuitEngine(_document).Analyze();
        var analyzer = new SafetyAnalyzer(_document, snapshot);
        var topologyDanger = analyzer.CheckTopology().FirstOrDefault(f => f.Severity == SafetySeverity.Danger);
        if (topologyDanger is not null) return topologyDanger;
        var meterFinding = analyzer.CheckMeter();
        return meterFinding?.Severity == SafetySeverity.Danger ? meterFinding : null;
    }

    private void CheckSafetyNow()
    {
        var finding = GetSafetyFinding();
        if (finding is null)
        {
            _activeSafetyKey = null;
            CircuitSurface.SafetyLocked = false;
            SafetyBorder.Visibility = Visibility.Collapsed;
            return;
        }
        ShowSafetyFinding(finding, force: false);
    }

    private void ShowSafetyFinding(SafetyFinding finding, bool force)
    {
        var key = $"{finding.Code}:{finding.Message}";
        var newFinding = _activeSafetyKey != key;
        _activeSafetyKey = key;
        SafetyBorder.Visibility = Visibility.Visible;
        if (finding.Severity == SafetySeverity.Danger && !_meterOpen) SetMeterPanel(true);
        SafetyTitle.Text = "SAFETY LOCK";
        SafetyText.Text = finding.Message;
        if (finding.Severity == SafetySeverity.Danger && (newFinding || force))
        {
            ConnectionGuideDrawer.Visibility = Visibility.Collapsed;
            CircuitSurface.SafetyLocked = true;
            SafetySounds.StopContinuity();
            if (newFinding) SafetySounds.Shock();
            StatusText.Text = "SAFETY LOCK — read the warning and correct the circuit or meter setup.";
        }
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CanvasScale is null) return;
        CanvasScale.ScaleX = e.NewValue;
        CanvasScale.ScaleY = e.NewValue;
        if (ZoomText is not null) ZoomText.Text = $"{e.NewValue * 100:0}%";
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ZoomPanel is null || DrawingToolsPanel is null || HeaderBrandColumn is null || PaletteColumn is null || MeterColumn is null) return;
        var compact = e.NewSize.Width < 1080;
        ZoomPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DrawingToolsPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        if (e.NewSize.Width < 900)
        {
            BrandPanel.Visibility = Visibility.Collapsed;
            HeaderBrandColumn.Width = new GridLength(0);
            if (_componentsOpen) PaletteColumn.Width = new GridLength(180);
            if (_meterOpen) MeterColumn.Width = new GridLength(270);
        }
        else
        {
            BrandPanel.Visibility = Visibility.Visible;
            HeaderBrandColumn.Width = new GridLength(compact ? 155 : 220);
            if (_componentsOpen) PaletteColumn.Width = new GridLength(compact ? 195 : 230);
            if (_meterOpen) MeterColumn.Width = new GridLength(compact ? 285 : 326);
        }
    }

    private void ToggleComponents_Click(object sender, RoutedEventArgs e) => SetComponentsPanel(!_componentsOpen);

    private void CloseComponents_Click(object sender, RoutedEventArgs e) => SetComponentsPanel(false);

    private void ToggleMeter_Click(object sender, RoutedEventArgs e) => SetMeterPanel(!_meterOpen);

    private void CloseMeter_Click(object sender, RoutedEventArgs e) => SetMeterPanel(false);

    private void SetComponentsPanel(bool open)
    {
        _componentsOpen = open;
        PaletteV2Panel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        PaletteColumn.MinWidth = 0;
        PaletteColumn.Width = open ? new GridLength(ActualWidth < 900 ? 180 : ActualWidth < 1080 ? 195 : 230) : new GridLength(0);
        StatusText.Text = open ? "Components panel opened." : "Components panel closed — playground expanded.";
    }

    private void SetMeterPanel(bool open)
    {
        _meterOpen = open;
        MeterPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        MeterColumn.MinWidth = 0;
        MeterColumn.Width = open ? new GridLength(ActualWidth < 900 ? 270 : ActualWidth < 1080 ? 285 : 326) : new GridLength(0);
        StatusText.Text = open ? "Multimeter panel opened." : "Multimeter panel closed — playground expanded.";
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!CanvasScrollViewer.IsMouseOver) return;
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            e.Handled = true;
            return;
        }
        var step = e.Delta > 0 ? 0.1 : -0.1;
        ZoomSlider.Value = Math.Clamp(Math.Round((ZoomSlider.Value + step) * 10) / 10, ZoomSlider.Minimum, ZoomSlider.Maximum);
        StatusText.Text = $"Zoom: {ZoomSlider.Value * 100:0}%";
        e.Handled = true;
    }

    private void CanvasScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var middlePan = e.ChangedButton == MouseButton.Middle;
        var backgroundPan = e.ChangedButton == MouseButton.Left && CircuitSurface.Mode == EditorMode.Select && !CircuitSurface.IsRepositioningWire &&
                            !IsCtrlDrag(Keyboard.Modifiers) && CircuitSurface.IsBackgroundAt(e.GetPosition(CircuitSurface));
        if (backgroundPan)
        {
            // Let the canvas receive the down event first so a plain click clears
            // selection. Promote it to a pan only after actual pointer movement.
            _pendingBackgroundPan = true;
            _panStart = e.GetPosition(CanvasScrollViewer);
            _panStartHorizontal = CanvasScrollViewer.HorizontalOffset;
            _panStartVertical = CanvasScrollViewer.VerticalOffset;
            return;
        }
        if (!middlePan) return;
        _isPanning = true;
        _panStart = e.GetPosition(CanvasScrollViewer);
        _panStartHorizontal = CanvasScrollViewer.HorizontalOffset;
        _panStartVertical = CanvasScrollViewer.VerticalOffset;
        CanvasScrollViewer.Cursor = Cursors.SizeAll;
        CanvasScrollViewer.CaptureMouse();
        e.Handled = true;
    }

    private void CanvasScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pendingBackgroundPan && e.LeftButton == MouseButtonState.Pressed)
        {
            // A canvas-owned gesture (for example rotating a text label) wins over
            // the pending background-pan candidate from the routed mouse-down.
            if (CircuitSurface.IsMouseCaptured)
            {
                _pendingBackgroundPan = false;
                return;
            }
            var candidate = e.GetPosition(CanvasScrollViewer);
            if (Math.Abs(candidate.X - _panStart.X) + Math.Abs(candidate.Y - _panStart.Y) < 5) return;
            _pendingBackgroundPan = false;
            _isPanning = true;
            CanvasScrollViewer.Cursor = Cursors.SizeAll;
            CanvasScrollViewer.CaptureMouse();
        }
        if (!_isPanning) return;
        var point = e.GetPosition(CanvasScrollViewer);
        var scale = Math.Max(0.1, CanvasScale.ScaleX);
        var horizontal = _panStartHorizontal - (point.X - _panStart.X);
        var vertical = _panStartVertical - (point.Y - _panStart.Y);
        if (horizontal < 160)
        {
            const double grow = 1200;
            CircuitSurface.ExpandWorkspaceBefore(grow, 0);
            horizontal += grow * scale;
        }
        if (vertical < 160)
        {
            const double grow = 900;
            CircuitSurface.ExpandWorkspaceBefore(0, grow);
            vertical += grow * scale;
        }
        CanvasScrollViewer.ScrollToHorizontalOffset(horizontal);
        CanvasScrollViewer.ScrollToVerticalOffset(vertical);
        e.Handled = true;
    }

    private void CanvasScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingBackgroundPan) _pendingBackgroundPan = false;
        if (!_isPanning) return;
        _isPanning = false;
        CanvasScrollViewer.ReleaseMouseCapture();
        CanvasScrollViewer.Cursor = Cursors.Arrow;
        StatusText.Text = "Canvas moved";
        e.Handled = true;
    }

    private void CanvasScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (CircuitSurface is null || CanvasScale is null) return;
        var scale = Math.Max(0.1, CanvasScale.ScaleX);
        var visibleBottomRight = CircuitSurface.ViewToWorld(new Point(
            (CanvasScrollViewer.HorizontalOffset + CanvasScrollViewer.ViewportWidth) / scale,
            (CanvasScrollViewer.VerticalOffset + CanvasScrollViewer.ViewportHeight) / scale));
        CircuitSurface.EnsureWorkspaceBeyond(visibleBottomRight.X + 350, visibleBottomRight.Y + 300);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsTextEntryFocused(e.OriginalSource) && !(e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))) return;
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z)
        {
            Redo();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            Undo();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.None && IsActive)
        {
            var mode = CanvasModeShortcut(e.Key);
            if (mode is { } canvasMode)
            {
                SetCanvasMode(canvasMode);
                e.Handled = true;
                return;
            }
        }
        var colorNumber = e.Key is >= Key.D1 and <= Key.D9 ? (int)e.Key - (int)Key.D0
            : e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? (int)e.Key - (int)Key.NumPad0
            : 0;
        if (CircuitSurface.Mode == EditorMode.Wire && Keyboard.Modifiers == ModifierKeys.None && colorNumber is >= 1 and <= 9)
        {
            SetWireColor(colorNumber);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            CircuitSurface.DeleteSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CircuitSurface.CancelAction();
            SelectModeButton.IsChecked = true;
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            SaveDocument(saveAs: true);
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveDocument();
            e.Handled = true;
        }
        else if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Open_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.N && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            New_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void SetCanvasMode(EditorMode mode)
    {
        var button = mode switch
        {
            EditorMode.Select => SelectModeButton,
            EditorMode.Wire => WireModeButton,
            EditorMode.Probe => MeterModeButton,
            _ => null
        };
        if (button is null) return;
        if (button.IsChecked == true) Mode_Checked(button, new RoutedEventArgs());
        else button.IsChecked = true;
        CircuitSurface.Focus();
    }

    private static bool IsTextEntryFocused(object? source)
        => source is TextBox or ComboBox or PasswordBox or RichTextBox;

    private static bool IsCtrlDrag(ModifierKeys modifiers) => modifiers.HasFlag(ModifierKeys.Control);

    private static EditorMode? CanvasModeShortcut(Key key) => key switch
    {
        Key.S => EditorMode.Select,
        Key.W => EditorMode.Wire,
        Key.D => EditorMode.Probe,
        _ => null
    };

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SafetySounds.StopContinuity();
        if (!ConfirmDiscard()) e.Cancel = true;
    }

    private bool ConfirmDiscard()
    {
        if (!_isDirty) return true;
        var result = MessageBox.Show(this, "Save your changes before continuing?", "Unsaved topology", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return false;
        return result != MessageBoxResult.Yes || SaveDocument();
    }

    private void RefreshAll()
    {
        _updatingUi = true;
        foreach (var item in MeterProfileCombo.Items.OfType<ComboBoxItem>())
            if (Equals(item.Tag, _document.Meter.Profile.ToString())) { MeterProfileCombo.SelectedItem = item; break; }
        foreach (var item in MeterModeCombo.Items.OfType<ComboBoxItem>())
        {
            if (Equals(item.Tag, _document.Meter.Mode.ToString()))
            {
                MeterModeCombo.SelectedItem = item;
                break;
            }
        }
        _updatingUi = false;
        PopulateMeterRanges();
        _lastSnapshot = new CircuitEngine(_document).Analyze();
        RefreshMeter(_lastSnapshot);
        RefreshHealth(_lastSnapshot);
        RefreshDocumentText();
        UpdateConnectionGuide();
        CircuitSurface.InvalidateVisual();
    }

    private void UpdateConnectionGuide()
    {
        if (ConnectionGuideDrawer is null || ConnectionGuideText is null) return;
        if (CircuitSurface.SelectedComponent is { } selected)
            ConnectionGuideText.Text = ConnectionGuide.Describe(_document, selected);
        else if (CircuitSurface.SelectedWire is { } wire)
        {
            var a = ComponentCatalog.GetPortLabel(_document, wire.A);
            var b = wire.HasOpenEnd
                ? $"open end · {wire.OpenEndX:0}, {wire.OpenEndY:0}"
                : ComponentCatalog.GetPortLabel(_document, wire.B);
            ConnectionGuideText.Text = $"WIRE · {wire.Color}\n\nA: {a}\nB: {b}\n\nThis guide follows the last selected object. An open wire endpoint is a usable connection point for probes and other wires.";
        }
        else if (CircuitSurface.SelectedAnnotation is { } annotation)
        {
            ConnectionGuideText.Text = $"PLAYGROUND ANNOTATION · {annotation.Type}\n\n{(string.IsNullOrWhiteSpace(annotation.Text) ? "Line or arrow" : annotation.Text)}\n\nColor: {annotation.Color}";
        }
        else if (ConnectionGuideText.Text.Length == 0)
            ConnectionGuideText.Text = "Select a component, wire, or annotation. This guide updates in the background and opens only when you press Connections.";
    }

    private void ToggleConnectionGuide_Click(object sender, RoutedEventArgs e)
    {
        UpdateConnectionGuide();
        ConnectionGuideDrawer.Visibility = ConnectionGuideDrawer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CloseConnectionGuide_Click(object sender, RoutedEventArgs e) => ConnectionGuideDrawer.Visibility = Visibility.Collapsed;

    private MeterReading RefreshMeter(CircuitSnapshot? snapshot = null)
    {
        var engine = new CircuitEngine(_document);
        snapshot ??= _lastSnapshot ??= engine.Analyze();
        var reading = engine.Measure(_document.Meter.Mode, _document.Meter.RedProbe, _document.Meter.BlackProbe, snapshot);
        var setup = new SafetyAnalyzer(_document, snapshot).CheckMeter();
        if (setup is { Severity: SafetySeverity.Caution })
        {
            reading = new MeterReading(setup.Code == "METER_OVER_RANGE" ? "OL" : "MODE",
                _document.Meter.Mode switch
                {
                    MeterMode.AcVoltage => "V AC",
                    MeterMode.DcVoltage => "V DC",
                    MeterMode.DcCurrent => "A DC",
                    _ => ""
                },
                setup.Message, Warning: true);
        }
        MeterDisplay.Text = reading.Display;
        MeterUnit.Text = reading.Unit.Length > 0 ? " " + reading.Unit : "";
        MeterDetail.Text = reading.Detail;
        MeterDisplay.Foreground = new SolidColorBrush(reading.Warning ? Color.FromRgb(255, 171, 118) : reading.Beep ? Color.FromRgb(126, 240, 154) : Color.FromRgb(215, 240, 190));
        RedProbeLabel.Text = ComponentCatalog.GetPortLabel(_document, _document.Meter.RedProbe);
        BlackProbeLabel.Text = ComponentCatalog.GetPortLabel(_document, _document.Meter.BlackProbe);
        if (reading.Beep)
        {
            if (!SafetySounds.IsContinuityPlaying) SafetySounds.Continuity();
        }
        else
            SafetySounds.StopContinuity();
        return reading;
    }

    private void RefreshHealth(CircuitSnapshot? snapshot = null)
    {
        var engine = new CircuitEngine(_document);
        snapshot ??= _lastSnapshot ??= engine.Analyze();
        var findings = new SafetyAnalyzer(_document, snapshot).CheckTopology();
        var messages = findings.Where(f => f.Severity == SafetySeverity.Danger)
            .Select(f => $"⚠ {f.Message}").Take(4).ToList();
        messages.AddRange(engine.GetHealthMessages(snapshot).Where(m => !m.StartsWith("⚠") && !m.Contains("Circuit ready")));
        if (messages.Count == 0) messages.Add("No modeled topology fault detected.");
        HealthText.Text = string.Join("\n", messages);
        var warning = findings.Any(f => f.Severity == SafetySeverity.Danger);
        HealthBorder.Background = new SolidColorBrush(warning ? Color.FromRgb(255, 243, 231) : Color.FromRgb(237, 247, 240));
        HealthBorder.BorderBrush = new SolidColorBrush(warning ? Color.FromRgb(239, 204, 166) : Color.FromRgb(206, 232, 213));
        HealthText.Foreground = new SolidColorBrush(warning ? Color.FromRgb(130, 85, 42) : Color.FromRgb(63, 103, 80));
    }

    private void RefreshDocumentText()
    {
        var marker = _isDirty ? " • unsaved" : "";
        var selected = CircuitSurface.SelectedComponent is { } component
            ? $"{FormatCategory(ComponentCatalog.GetCategory(component.Type))} · {component.Name} · {component.Rotation:0.#}°"
            : CircuitSurface.SelectedWire is not null ? "wire selected"
            : CircuitSurface.SelectedAnnotation is not null ? "annotation selected"
            : $"{_document.Components.Count} components · {_document.Wires.Count} wires · {_document.Annotations.Count} annotations";
        DocumentText.Text = $"{_document.Name}{marker}   |   {selected}";
        Title = $"{(_isDirty ? "*" : "")}{_document.Name} — ElectroSketch";
    }

    private static string FormatCategory(ComponentCategory category)
    {
        var text = category.ToString();
        for (var i = 1; i < text.Length; i++)
            if (char.IsUpper(text[i]) && char.IsLower(text[i - 1])) text = text.Insert(i++, " ");
        return text;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? "topology" : name;
    }

    private void FitWindowToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, Math.Max(560, area.Width - 32));
        MinHeight = Math.Min(MinHeight, Math.Max(440, area.Height - 32));
        Width = Math.Min(1180, Math.Max(MinWidth, area.Width - 64));
        Height = Math.Min(760, Math.Max(MinHeight, area.Height - 64));
    }

    private void LoadGlobalNotes()
    {
        _loadingNotes = true;
        _notes = NotesStore.Load();
        NotesTextBox.Text = _notes.Text;
        _noteImages.Clear();
        foreach (var item in _notes.Images) _noteImages.Add(item);
        NotesImagesList.ItemsSource = _noteImages;
        _loadingNotes = false;
    }

    private void SaveGlobalNotes()
    {
        if (_loadingNotes) return;
        try
        {
            _notes.Text = NotesTextBox.Text;
            _notes.Images = _noteImages.ToList();
            NotesStore.Save(_notes);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Notes could not be saved: {ex.Message}";
        }
    }

    private void ToggleNotes_Click(object sender, RoutedEventArgs e)
        => NotesDrawer.Visibility = NotesDrawer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void CloseNotes_Click(object sender, RoutedEventArgs e) => NotesDrawer.Visibility = Visibility.Collapsed;

    private void NotesTextBox_TextChanged(object sender, TextChangedEventArgs e) => SaveGlobalNotes();

    private void LoadNoteImages_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add images to ElectroSketch notes",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            foreach (var item in NotesStore.ImportImages(dialog.FileNames)) _noteImages.Add(item);
            SaveGlobalNotes();
            StatusText.Text = $"Added {dialog.FileNames.Length} image(s) to global notes.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The images could not be added.\n\n{ex.Message}", "Notes", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveNoteImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NoteImageItem item }) return;
        _noteImages.Remove(item);
        SaveGlobalNotes();
        StatusText.Text = "Image removed from notes. The original file was not deleted.";
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
