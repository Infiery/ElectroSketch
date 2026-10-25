using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ElectroSketch.Core;

namespace ElectroSketch.Controls;

public sealed class CircuitCanvas : FrameworkElement
{
    private CircuitDocument _document = new();
    private ComponentModel? _selectedComponent;
    private WireModel? _selectedWire;
    private AnnotationModel? _selectedAnnotation;
    private readonly HashSet<ComponentModel> _selectedComponents = [];
    private readonly HashSet<WireModel> _selectedWires = [];
    private readonly HashSet<AnnotationModel> _selectedAnnotations = [];
    private ComponentModel? _dragComponent;
    private Dictionary<ComponentModel, Point> _dragComponentStarts = [];
    private Dictionary<AnnotationModel, (Point Start, Point End)> _dragAnnotationStarts = [];
    private Point _dragMouseStart;
    private bool _didDrag;
    private bool _selectionBoxActive;
    private Point _selectionBoxStart;
    private Point _selectionBoxCurrent;
    private ComponentModel? _selectedGroupComponent;
    private string? _selectedGroupId;
    private ComponentModel? _dragGroupComponent;
    private string? _dragGroupId;
    private Point _dragGroupStart;
    private Point _dragGroupOffsetStart;
    private ComponentModel? _rotateGroupComponent;
    private string? _rotateGroupId;
    private double _groupRotationPointerOffset;
    private AnnotationModel? _dragAnnotation;
    private AnnotationModel? _rotateAnnotation;
    private Point _rotateAnnotationCenter;
    private double _annotationRotationPointerOffset;
    private PortReference? _wireStart;
    private Point _mousePosition;
    private PortReference? _hoverPort;
    private bool _drawingAnnotation;
    private Point _annotationStart;
    private Point _annotationCurrent;
    private ComponentModel? _rotateComponent;
    private double _rotationPointerOffset;
    private WireModel? _liftedWire;
    private WireModel? _liftedOriginal;
    private int _liftedWireIndex;
    private double _workspaceOffsetX;
    private double _workspaceOffsetY;
    private readonly DispatcherTimer _deviceTimer;

    public event EventHandler? DocumentChanged;
    public event EventHandler? ProbeChanged;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler<Point>? TextRequested;
    public event EventHandler<AnnotationModel>? AnnotationEditRequested;
    public event Action<double, double>? WorkspaceExpandedBefore;

    public CircuitDocument Document
    {
        get => _document;
        set
        {
            _document = value ?? new CircuitDocument();
            _selectedComponent = null;
            _selectedWire = null;
            _selectedAnnotation = null;
            _selectedComponents.Clear();
            _selectedWires.Clear();
            _selectedAnnotations.Clear();
            _selectedGroupComponent = null;
            _selectedGroupId = null;
            _wireStart = null;
            InvalidateVisual();
        }
    }

    public EditorMode Mode { get; set; } = EditorMode.Select;
    public string WireColor { get; set; } = "#20262D";
    public string AnnotationColor { get; set; } = "#34495E";
    public bool PlaceRedProbe { get; set; } = true;
    public ComponentModel? SelectedComponent => _selectedComponent;
    public WireModel? SelectedWire => _selectedWire;
    public AnnotationModel? SelectedAnnotation => _selectedAnnotation;
    public bool IsRepositioningWire => _liftedWire is not null;
    public bool SafetyLocked { get; set; }
    public double WorkspaceOffsetX => _workspaceOffsetX;
    public double WorkspaceOffsetY => _workspaceOffsetY;
    public Point ViewToWorld(Point point) => new(point.X - _workspaceOffsetX, point.Y - _workspaceOffsetY);

    public CircuitCanvas()
    {
        Focusable = true;
        Width = 3200;
        Height = 2000;
        Cursor = Cursors.Arrow;
        SnapsToDevicePixels = true;
        _deviceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _deviceTimer.Tick += (_, _) => AdvanceRechargeableDevices();
        _deviceTimer.Start();
    }

    public void CancelAction()
    {
        RestoreLiftedWire();
        _wireStart = null;
        _dragComponent = null;
        _rotateComponent = null;
        _rotateGroupComponent = null;
        _rotateAnnotation = null;
        _dragGroupComponent = null;
        _dragAnnotation = null;
        _selectionBoxActive = false;
        _drawingAnnotation = false;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    public void DeleteSelection()
    {
        ComponentModel[] selectedComponents = _selectedComponents.Count > 0 ? _selectedComponents.ToArray()
            : _selectedComponent is null ? [] : [_selectedComponent];
        WireModel[] selectedWires = _selectedWires.Count > 0 ? _selectedWires.ToArray()
            : _selectedWire is null ? [] : [_selectedWire];
        AnnotationModel[] selectedAnnotations = _selectedAnnotations.Count > 0 ? _selectedAnnotations.ToArray()
            : _selectedAnnotation is null ? [] : [_selectedAnnotation];
        if (selectedComponents.Length == 0 && selectedWires.Length == 0 && selectedAnnotations.Length == 0) return;
        var componentIds = selectedComponents.Select(component => component.Id).ToHashSet(StringComparer.Ordinal);
        var attachedWires = _document.Wires.Where(w => componentIds.Contains(w.A.ComponentId) ||
            (!w.HasOpenEnd && componentIds.Contains(w.B.ComponentId)));
        var removedWireIds = RemoveWireFamily(selectedWires.Concat(attachedWires).DistinctBy(wire => wire.Id));
        _document.Components.RemoveAll(component => componentIds.Contains(component.Id));
        if (_document.Meter.RedProbe is { } red && componentIds.Contains(red.ComponentId)) _document.Meter.RedProbe = null;
        if (_document.Meter.BlackProbe is { } black && componentIds.Contains(black.ComponentId)) _document.Meter.BlackProbe = null;
        foreach (var annotation in selectedAnnotations) _document.Annotations.Remove(annotation);
        ClearSelection();
        var removedCount = selectedComponents.Length + selectedAnnotations.Length + removedWireIds.Count;
        NotifyDocumentChanged($"Removed {removedCount} selected object{(removedCount == 1 ? "" : "s")}");
    }

    private HashSet<string> RemoveWireFamily(IEnumerable<WireModel> initial)
    {
        var removed = initial.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var wire in _document.Wires)
            {
                if (removed.Contains(wire.Id)) continue;
                var dependsOnRemovedOpenEnd =
                    (wire.A.PortId == "OPEN" && removed.Contains(wire.A.ComponentId)) ||
                    (!wire.HasOpenEnd && wire.B.PortId == "OPEN" && removed.Contains(wire.B.ComponentId));
                if (dependsOnRemovedOpenEnd) grew |= removed.Add(wire.Id);
            }
        }
        _document.Wires.RemoveAll(w => removed.Contains(w.Id));
        if (_document.Meter.RedProbe is { } red && removed.Contains(red.ComponentId)) _document.Meter.RedProbe = null;
        if (_document.Meter.BlackProbe is { } black && removed.Contains(black.ComponentId)) _document.Meter.BlackProbe = null;
        return removed;
    }

    public void SelectAllNone()
    {
        ClearSelection();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void ClearSelection()
    {
        _selectedComponent = null;
        _selectedWire = null;
        _selectedAnnotation = null;
        _selectedComponents.Clear();
        _selectedWires.Clear();
        _selectedAnnotations.Clear();
        _selectedGroupComponent = null;
        _selectedGroupId = null;
    }

    private void SelectComponent(ComponentModel component)
    {
        ClearSelection();
        _selectedComponent = component;
        _selectedComponents.Add(component);
        InvalidateVisual();
    }

    private void SelectWire(WireModel wire)
    {
        ClearSelection();
        _selectedWire = wire;
        _selectedWires.Add(wire);
        InvalidateVisual();
    }

    private void SelectAnnotation(AnnotationModel annotation)
    {
        ClearSelection();
        _selectedAnnotation = annotation;
        _selectedAnnotations.Add(annotation);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void SelectContactGroup(ComponentModel component, string groupId)
    {
        SelectComponent(component);
        _selectedGroupComponent = component;
        _selectedGroupId = groupId;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void SelectItemsInside(Rect selection)
    {
        ClearSelection();
        foreach (var component in _document.Components)
            if (selection.IntersectsWith(GetComponentWorldBounds(component))) _selectedComponents.Add(component);
        foreach (var wire in _document.Wires)
            if (selection.IntersectsWith(GetWireWorldBounds(wire))) _selectedWires.Add(wire);
        foreach (var annotation in _document.Annotations)
            if (selection.IntersectsWith(GetAnnotationWorldBounds(annotation))) _selectedAnnotations.Add(annotation);
        _selectedComponent = _selectedComponents.FirstOrDefault();
        _selectedWire = _selectedWires.FirstOrDefault();
        _selectedAnnotation = _selectedAnnotations.FirstOrDefault();
        var count = _selectedComponents.Count + _selectedWires.Count + _selectedAnnotations.Count;
        StatusChanged?.Invoke(this, count == 0 ? "No objects inside the selection box." : $"Selected {count} objects. Press Delete to remove them.");
    }

    private Rect GetComponentWorldBounds(ComponentModel component)
    {
        var size = ComponentCatalog.GetSize(component);
        var corners = new[] { new Point(0, 0), new Point(size.Width, 0), new Point(size.Width, size.Height), new Point(0, size.Height) }
            .Select(point => ComponentCatalog.LocalToWorld(component, point)).ToArray();
        var bounds = new Rect(corners[0], corners[0]);
        foreach (var corner in corners.Skip(1)) bounds.Union(corner);
        return bounds;
    }

    private Rect GetWireWorldBounds(WireModel wire)
    {
        var a = ComponentCatalog.GetAbsolutePortPosition(_document, wire.A);
        var b = wire.HasOpenEnd ? new Point(wire.OpenEndX, wire.OpenEndY) : ComponentCatalog.GetAbsolutePortPosition(_document, wire.B);
        var bounds = new Rect(a, b);
        bounds.Inflate(8, 8);
        return bounds;
    }

    private Rect GetAnnotationWorldBounds(AnnotationModel annotation)
    {
        if (annotation.Type != AnnotationType.Text)
        {
            var bounds = new Rect(new Point(annotation.X1, annotation.Y1), new Point(annotation.X2, annotation.Y2));
            bounds.Inflate(8, 8);
            return bounds;
        }
        var text = MakeText(annotation.Text, 17, FontWeights.SemiBold, Brushes.Black);
        var origin = new Point(annotation.X1, annotation.Y1);
        var localBounds = AnnotationSelectionBounds(annotation, text);
        var corners = new[] { new Point(localBounds.Left, localBounds.Top), new Point(localBounds.Right, localBounds.Top),
            new Point(localBounds.Right, localBounds.Bottom), new Point(localBounds.Left, localBounds.Bottom) }
            .Select(point => RotatePoint(point, origin, annotation.Rotation)).ToArray();
        var boundsText = new Rect(corners[0], corners[0]);
        foreach (var corner in corners.Skip(1)) boundsText.Union(corner);
        boundsText.Inflate(6, 6);
        return boundsText;
    }

    public void SetSelectedAnnotationColor(string color)
    {
        if (_selectedAnnotation is { Type: AnnotationType.Text } selected)
        {
            selected.Color = color;
            NotifyDocumentChanged("Text color changed");
        }
        else
        {
            AnnotationColor = color;
            InvalidateVisual();
        }
    }

    public void UpdateTextAnnotation(AnnotationModel annotation, string text)
    {
        if (!_document.Annotations.Contains(annotation) || string.IsNullOrWhiteSpace(text)) return;
        annotation.Text = text.Trim();
        NotifyDocumentChanged("Text annotation updated");
    }

    public bool IsBackgroundAt(Point point)
    {
        var world = ViewToWorld(point);
        return HitPort(world) is null && HitComponent(world) is null && HitWire(world) is null &&
               HitAnnotation(world) is null && HitAnnotationRotationHandle(world) is null;
    }

    public void AddComponentAt(ComponentType type, Point center)
    {
        var size = ComponentCatalog.GetSize(type);
        var x = Snap(Math.Clamp(center.X - size.Width / 2, -_workspaceOffsetX + 10, Width - _workspaceOffsetX - size.Width - 10));
        var y = Snap(Math.Clamp(center.Y - size.Height / 2, -_workspaceOffsetY + 10, Height - _workspaceOffsetY - size.Height - 10));
        var component = ComponentCatalog.Create(type, x, y);
        _document.Components.Add(component);
        SelectComponent(component);
        Mode = EditorMode.Select;
        EnsureWorkspaceBeyond(x + size.Width + 400, y + size.Height + 300);
        var connected = ComponentCatalog.IsReadyCable(type) ? AutoConnectCable(component) : 0;
        NotifyDocumentChanged(connected > 0
            ? $"Added {component.Name}; {connected} suitable contacts connected. You can edit the wiring in Wire mode."
            : $"Added {component.Name}. Drag it to position it, or connect its round contacts in Wire mode.");
    }

    public void ClearProbes()
    {
        _document.Meter.RedProbe = null;
        _document.Meter.BlackProbe = null;
        PlaceRedProbe = false;
        ProbeChanged?.Invoke(this, EventArgs.Empty);
        StatusChanged?.Invoke(this, "Meter probes removed.");
        InvalidateVisual();
    }

    public void AddTextAnnotation(Point point, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var annotation = new AnnotationModel
        {
            Type = AnnotationType.Text,
            X1 = Snap(point.X),
            Y1 = Snap(point.Y),
            Text = text.Trim(),
            Color = AnnotationColor
        };
        _document.Annotations.Add(annotation);
        SelectAnnotation(annotation);
        NotifyDocumentChanged("Text annotation added");
    }

    public void EnsureWorkspaceBeyond(double right, double bottom)
    {
        var changed = false;
        while (right > Width - _workspaceOffsetX - 300)
        {
            Width += 1200;
            changed = true;
        }
        while (bottom > Height - _workspaceOffsetY - 250)
        {
            Height += 900;
            changed = true;
        }
        if (changed) InvalidateVisual();
    }

    public void ExpandWorkspaceBefore(double width, double height)
    {
        if (width <= 0 && height <= 0) return;
        var dx = Math.Max(0, width);
        var dy = Math.Max(0, height);
        _workspaceOffsetX += dx;
        _workspaceOffsetY += dy;
        Width += dx;
        Height += dy;
        WorkspaceExpandedBefore?.Invoke(dx, dy);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        DrawBackground(dc);
        dc.PushTransform(new TranslateTransform(_workspaceOffsetX, _workspaceOffsetY));
        var snapshot = new CircuitEngine(_document).Analyze();
        foreach (var component in _document.Components)
            DrawComponent(dc, component, snapshot);
        foreach (var wire in _document.Wires) DrawWire(dc, wire, _selectedWires.Contains(wire) || wire == _selectedWire);
        if (_wireStart is not null)
            DrawLooseWire(dc, ComponentCatalog.GetAbsolutePortPosition(_document, _wireStart), _mousePosition, WireColor);
        foreach (var annotation in _document.Annotations.Where(annotation => !_selectedAnnotations.Contains(annotation) && annotation != _selectedAnnotation))
            DrawAnnotation(dc, annotation, false);
        if (_drawingAnnotation) DrawAnnotationPreview(dc);
        foreach (var component in _document.Components)
            DrawComponentOverlay(dc, component, _selectedComponents.Contains(component) || component == _selectedComponent);
        // Keep the selected label and its rotation handle above components/wires so
        // overlap cannot hide the handle or intercept its click.
        foreach (var annotation in _document.Annotations.Where(annotation => _selectedAnnotations.Contains(annotation) || annotation == _selectedAnnotation))
            DrawAnnotation(dc, annotation, true);
        if (_selectionBoxActive) DrawSelectionBox(dc);
        DrawProbeMarker(dc, _document.Meter.BlackProbe, "COM", Color.FromRgb(29, 34, 40));
        DrawProbeMarker(dc, _document.Meter.RedProbe, "VΩ", Color.FromRgb(229, 57, 53));
        DrawHoverTooltip(dc);
        dc.Pop();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var point = ViewToWorld(e.GetPosition(this));
        _mousePosition = point;

        if (SafetyLocked)
        {
            StatusChanged?.Invoke(this, "Safety lock is active. Reset it from the meter panel after reading the warning.");
            e.Handled = true;
            return;
        }

        if (Mode == EditorMode.Probe && e.ChangedButton == MouseButton.Right)
        {
            ClearProbes();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        if (Mode == EditorMode.AnnotationText)
        {
            TextRequested?.Invoke(this, point);
            e.Handled = true;
            return;
        }

        if (Mode is EditorMode.DrawLine or EditorMode.DrawArrow)
        {
            _annotationStart = point;
            _annotationCurrent = point;
            _drawingAnnotation = true;
            CaptureMouse();
            StatusChanged?.Invoke(this, Mode == EditorMode.DrawArrow ? "Drag to draw an arrow." : "Drag to draw a line.");
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (Mode == EditorMode.Select && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _selectionBoxStart = point;
            _selectionBoxCurrent = point;
            _selectionBoxActive = true;
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (Mode == EditorMode.Select && HitAnnotationRotationHandle(point) is { } annotationRotation)
        {
            SelectAnnotation(annotationRotation);
            _rotateAnnotation = annotationRotation;
            _rotateAnnotationCenter = new Point(annotationRotation.X1, annotationRotation.Y1);
            var pointerAngle = Math.Atan2(point.Y - _rotateAnnotationCenter.Y, point.X - _rotateAnnotationCenter.X) * 180 / Math.PI;
            _annotationRotationPointerOffset = annotationRotation.Rotation - pointerAngle;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (Mode == EditorMode.Select && HitAnnotation(point) is { } annotationHit)
        {
            if (e.ClickCount >= 2 && annotationHit.Type == AnnotationType.Text)
            {
                AnnotationEditRequested?.Invoke(this, annotationHit);
                e.Handled = true;
                return;
            }
            if (!_selectedAnnotations.Contains(annotationHit)) SelectAnnotation(annotationHit);
            _dragAnnotation = annotationHit;
            _dragMouseStart = point;
            _dragAnnotationStarts = _selectedAnnotations.ToDictionary(item => item, item =>
                (new Point(item.X1, item.Y1), new Point(item.X2, item.Y2)));
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (Mode == EditorMode.Select && HitContactGroupRotationHandle(point) is { } groupRotation)
        {
            SelectContactGroup(groupRotation.Component, groupRotation.GroupId);
            _rotateGroupComponent = groupRotation.Component;
            _rotateGroupId = groupRotation.GroupId;
            var groupState = ComponentCatalog.GetOrCreateContactGroup(_rotateGroupComponent, groupRotation.GroupId);
            var center = ComponentCatalog.LocalToWorld(_rotateGroupComponent,
                ComponentCatalog.GetContactGroupCenterLocal(_rotateGroupComponent, groupRotation.GroupId));
            var pointerAngle = Math.Atan2(point.Y - center.Y, point.X - center.X) * 180 / Math.PI;
            _groupRotationPointerOffset = groupState.Rotation - pointerAngle;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (Mode == EditorMode.Select && HitContactGroupBody(point) is { } groupHit && HitPort(point) is null)
        {
            SelectContactGroup(groupHit.Component, groupHit.GroupId);
            _dragGroupComponent = groupHit.Component;
            _dragGroupId = groupHit.GroupId;
            _dragGroupStart = point;
            var group = ComponentCatalog.GetOrCreateContactGroup(groupHit.Component, groupHit.GroupId);
            _dragGroupOffsetStart = new Point(group.OffsetX, group.OffsetY);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        var port = HitPort(point);
        if (Mode == EditorMode.Select && HitOpenWireEnd(point) is { } openWire)
        {
            BeginOpenWireLift(openWire, point);
            e.Handled = true;
            return;
        }

        if (Mode == EditorMode.Select && HitPowerSwitch(point) is { } strip)
        {
            var on = !strip.Properties.TryGetValue("Power", out var state) || !state.Equals("On", StringComparison.OrdinalIgnoreCase);
            strip.Properties["Power"] = on ? "On" : "Off";
            NotifyDocumentChanged($"{strip.Name} switched {(on ? "on" : "off")}");
            return;
        }
        if (Mode == EditorMode.Select && _liftedWire is not null)
        {
            CompleteLiftedWire(port, point);
            e.Handled = true;
            return;
        }

        if (Mode == EditorMode.Select && HitRotationHandle(point) is { } rotateComponent)
        {
            _rotateComponent = rotateComponent;
            var center = GetComponentCenter(rotateComponent);
            var pointerAngle = Math.Atan2(point.Y - center.Y, point.X - center.X) * 180 / Math.PI;
            _rotationPointerOffset = rotateComponent.Rotation - pointerAngle;
            SelectComponent(rotateComponent);
            CaptureMouse();
            StatusChanged?.Invoke(this, $"Rotate {rotateComponent.Name}: {rotateComponent.Rotation:0.#}°");
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (Mode == EditorMode.Select && HitWorkButton(point) is { } device)
        {
            UseDevice(device);
            e.Handled = true;
            return;
        }

        var toggle = HitViewToggle(point);
        if (toggle is not null)
        {
            var center = GetComponentCenter(toggle);
            ComponentCatalog.ToggleViewMode(toggle);
            var toggledSize = ComponentCatalog.GetSize(toggle);
            toggle.X = center.X - toggledSize.Width / 2;
            toggle.Y = center.Y - toggledSize.Height / 2;
            SelectComponent(toggle);
            NotifyDocumentChanged(ComponentCatalog.IsImageMode(toggle) ? "Physical connector view" : "Electronics pin view");
            return;
        }

        if (Mode == EditorMode.Wire)
        {
            if (port is null)
            {
                if (_wireStart is null)
                {
                    StatusChanged?.Invoke(this, "Click a round connection point to start a wire.");
                }
                else
                {
                    _document.Wires.Add(new WireModel
                    {
                        A = _wireStart.Copy(),
                        B = new PortReference(),
                        Color = WireColor,
                        HasOpenEnd = true,
                        OpenEndX = Snap(point.X),
                        OpenEndY = Snap(point.Y)
                    });
                    _wireStart = null;
                    NotifyDocumentChanged("Open-ended wire placed");
                }
                InvalidateVisual();
                return;
            }
            if (_wireStart is null)
            {
                _wireStart = port;
                StatusChanged?.Invoke(this, $"Wire started at {ComponentCatalog.GetPortLabel(_document, port)} — choose the other end.");
            }
            else
            {
                if (_wireStart.ComponentId == port.ComponentId && _wireStart.PortId == port.PortId)
                {
                    _wireStart = null;
                    StatusChanged?.Invoke(this, "Wire cancelled.");
                }
                else if (!WireExists(_wireStart, port))
                {
                    _document.Wires.Add(new WireModel { A = _wireStart.Copy(), B = port.Copy(), Color = WireColor });
                    _wireStart = null;
                    NotifyDocumentChanged("Wire connected");
                }
                else
                {
                    _wireStart = null;
                    StatusChanged?.Invoke(this, "Those points are already connected.");
                }
            }
            InvalidateVisual();
            return;
        }

        if (Mode == EditorMode.Probe)
        {
            if (port is null)
            {
                StatusChanged?.Invoke(this, "Probe mode: click a round connection point.");
                return;
            }
            if (PlaceRedProbe)
                _document.Meter.RedProbe = port.Copy();
            else
                _document.Meter.BlackProbe = port.Copy();
            PlaceRedProbe = !PlaceRedProbe;
            ProbeChanged?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, $"{(PlaceRedProbe ? "Black" : "Red")} probe placed. Now place the {(PlaceRedProbe ? "red" : "black")} probe.");
            InvalidateVisual();
            return;
        }

        if (Mode == EditorMode.Select && port is not null && BeginWireLift(port, point))
        {
            e.Handled = true;
            return;
        }

        var wireHit = HitWire(point);
        if (Mode == EditorMode.Select && wireHit is not null)
        {
            SelectWire(wireHit);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var component = HitComponent(point);
        if (component is not null)
        {
            if (!_selectedComponents.Contains(component)) SelectComponent(component);
            _dragComponent = component;
            _dragMouseStart = point;
            _dragComponentStarts = _selectedComponents.ToDictionary(item => item, item => new Point(item.X, item.Y));
            _dragAnnotationStarts = _selectedAnnotations.ToDictionary(item => item, item =>
                (new Point(item.X1, item.Y1), new Point(item.X2, item.Y2)));
            _didDrag = false;
            CaptureMouse();
        }
        else
        {
            var hitWire = HitWire(point);
            if (hitWire is not null) SelectWire(hitWire);
            else ClearSelection();
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mousePosition = ViewToWorld(e.GetPosition(this));
        var hover = HitPort(_mousePosition);
        if (!SamePort(hover, _hoverPort))
        {
            _hoverPort = hover;
            InvalidateVisual();
        }

        if (_selectionBoxActive && e.LeftButton == MouseButtonState.Pressed)
        {
            _selectionBoxCurrent = _mousePosition;
            InvalidateVisual();
        }
        else if (_rotateGroupComponent is not null && _rotateGroupId is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            var groupState = ComponentCatalog.GetOrCreateContactGroup(_rotateGroupComponent, _rotateGroupId);
            var center = ComponentCatalog.GetContactGroupCenterLocal(_rotateGroupComponent, _rotateGroupId);
            var localPointer = ComponentCatalog.WorldToLocal(_rotateGroupComponent, _mousePosition);
            var pointerAngle = Math.Atan2(localPointer.Y - center.Y, localPointer.X - center.X) * 180 / Math.PI;
            groupState.Rotation = SnapAngle(pointerAngle + _groupRotationPointerOffset);
            ComponentCatalog.ConstrainContactGroup(_rotateGroupComponent, _rotateGroupId);
            InvalidateVisual();
        }
        else if (_rotateAnnotation is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            _rotateAnnotation.Rotation = AnnotationRotationAtPointer(_rotateAnnotationCenter, _mousePosition, _annotationRotationPointerOffset);
            InvalidateVisual();
        }
        else if (_dragGroupComponent is not null && _dragGroupId is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            var group = ComponentCatalog.GetOrCreateContactGroup(_dragGroupComponent, _dragGroupId);
            var startLocal = ComponentCatalog.WorldToLocal(_dragGroupComponent, _dragGroupStart);
            var nowLocal = ComponentCatalog.WorldToLocal(_dragGroupComponent, _mousePosition);
            group.OffsetX = _dragGroupOffsetStart.X + nowLocal.X - startLocal.X;
            group.OffsetY = _dragGroupOffsetStart.Y + nowLocal.Y - startLocal.Y;
            ComponentCatalog.ConstrainContactGroup(_dragGroupComponent, _dragGroupId);
            InvalidateVisual();
        }
        else if (_dragAnnotation is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            var delta = _mousePosition - _dragMouseStart;
            foreach (var (annotation, start) in _dragAnnotationStarts)
            {
                annotation.X1 = start.Start.X + delta.X;
                annotation.Y1 = start.Start.Y + delta.Y;
                if (annotation.Type != AnnotationType.Text)
                {
                    annotation.X2 = start.End.X + delta.X;
                    annotation.Y2 = start.End.Y + delta.Y;
                }
            }
            InvalidateVisual();
        }
        else if (_rotateComponent is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            var center = GetComponentCenter(_rotateComponent);
            var pointerAngle = Math.Atan2(_mousePosition.Y - center.Y, _mousePosition.X - center.X) * 180 / Math.PI;
            var angle = NormalizeAngle(pointerAngle + _rotationPointerOffset);
            var nearestRightAngle = Math.Round(angle / 90) * 90;
            var distance = Math.Abs(NormalizeSignedAngle(angle - nearestRightAngle));
            if (distance <= 5) angle = NormalizeAngle(nearestRightAngle);
            _rotateComponent.Rotation = Math.Round(angle, 1);
            var radius = Math.Sqrt(Math.Pow(ComponentCatalog.GetSize(_rotateComponent).Width, 2) + Math.Pow(ComponentCatalog.GetSize(_rotateComponent).Height, 2)) / 2;
            EnsureWorkspaceBeyond(center.X + radius + 300, center.Y + radius + 250);
            StatusChanged?.Invoke(this, $"Rotation: {_rotateComponent.Rotation:0.#}°");
            InvalidateVisual();
        }
        else if (_dragComponent is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            var delta = _mousePosition - _dragMouseStart;
            foreach (var (component, start) in _dragComponentStarts)
            {
                var size = ComponentCatalog.GetSize(component);
                EnsureWorkspaceBeyond(start.X + delta.X + size.Width + 400, start.Y + delta.Y + size.Height + 300);
                component.X = Snap(Math.Clamp(start.X + delta.X, -_workspaceOffsetX + 10, Width - _workspaceOffsetX - size.Width - 10));
                component.Y = Snap(Math.Clamp(start.Y + delta.Y, -_workspaceOffsetY + 10, Height - _workspaceOffsetY - size.Height - 10));
            }
            foreach (var (annotation, start) in _dragAnnotationStarts)
            {
                annotation.X1 = start.Start.X + delta.X;
                annotation.Y1 = start.Start.Y + delta.Y;
                if (annotation.Type != AnnotationType.Text)
                {
                    annotation.X2 = start.End.X + delta.X;
                    annotation.Y2 = start.End.Y + delta.Y;
                }
            }
            _didDrag = true;
            InvalidateVisual();
        }
        else if (_drawingAnnotation && e.LeftButton == MouseButtonState.Pressed)
        {
            _annotationCurrent = _mousePosition;
            EnsureWorkspaceBeyond(_mousePosition.X + 400, _mousePosition.Y + 300);
            InvalidateVisual();
        }
        else
        {
            Cursor = hover is not null && Mode != EditorMode.Select ? Cursors.Hand : Cursors.Arrow;
            if (_wireStart is not null) InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_selectionBoxActive)
        {
            _selectionBoxCurrent = ViewToWorld(e.GetPosition(this));
            _selectionBoxActive = false;
            SelectItemsInside(new Rect(_selectionBoxStart, _selectionBoxCurrent));
            ReleaseMouseCapture();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
        else if (_dragGroupComponent is not null && _dragGroupId is not null)
        {
            var component = _dragGroupComponent;
            var groupId = _dragGroupId;
            _dragGroupComponent = null;
            _dragGroupId = null;
            ReleaseMouseCapture();
            var groupLabel = ComponentCatalog.TryGetContactGroup(component, groupId, out var group) ? group.Label : "Contact group";
            NotifyDocumentChanged($"{groupLabel} moved");
        }
        else if (_rotateGroupComponent is not null && _rotateGroupId is not null)
        {
            var component = _rotateGroupComponent;
            var groupId = _rotateGroupId;
            var rotation = ComponentCatalog.GetOrCreateContactGroup(component, groupId).Rotation;
            _rotateGroupComponent = null;
            _rotateGroupId = null;
            ReleaseMouseCapture();
            NotifyDocumentChanged($"Contact group rotated to {rotation:0.#}°");
        }
        else if (_rotateAnnotation is not null)
        {
            var annotation = _rotateAnnotation;
            _rotateAnnotation = null;
            ReleaseMouseCapture();
            NotifyDocumentChanged($"Text rotated to {annotation.Rotation:0.#}°");
        }
        else if (_dragAnnotation is not null)
        {
            _dragAnnotation = null;
            ReleaseMouseCapture();
            NotifyDocumentChanged("Annotation moved");
        }
        else if (_dragComponent is not null)
        {
            var moved = _dragComponent;
            _dragComponent = null;
            ReleaseMouseCapture();
            if (_didDrag)
            {
                var connections = 0;
                foreach (var component in _dragComponentStarts.Keys)
                    if (ComponentCatalog.IsReadyCable(component.Type)) connections += AutoConnectCable(component);
                NotifyDocumentChanged(connections > 0 ? $"Cable placed — {connections} compatible contacts connected" : "Component moved");
            }
            _dragComponentStarts.Clear();
            _dragAnnotationStarts.Clear();
            _didDrag = false;
        }
        else if (_rotateComponent is not null)
        {
            var name = _rotateComponent.Name;
            var angle = _rotateComponent.Rotation;
            _rotateComponent = null;
            ReleaseMouseCapture();
            NotifyDocumentChanged($"{name} rotated to {angle:0.#}°");
        }
        else if (_drawingAnnotation)
        {
            _drawingAnnotation = false;
            ReleaseMouseCapture();
            if ((_annotationCurrent - _annotationStart).Length > 5)
            {
                var annotation = new AnnotationModel
                {
                    Type = Mode == EditorMode.DrawArrow ? AnnotationType.Arrow : AnnotationType.Line,
                    X1 = Snap(_annotationStart.X),
                    Y1 = Snap(_annotationStart.Y),
                    X2 = Snap(_annotationCurrent.X),
                    Y2 = Snap(_annotationCurrent.Y),
                    Color = WireColor
                };
                _document.Annotations.Add(annotation);
                SelectAnnotation(annotation);
                NotifyDocumentChanged(annotation.Type == AnnotationType.Arrow ? "Arrow added" : "Line added");
            }
            else
            {
                StatusChanged?.Invoke(this, "Drag farther to add the annotation.");
                InvalidateVisual();
            }
        }
    }

    private void DrawBackground(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(248, 249, 250)), null, new Rect(0, 0, Width, Height));
        var tile = new DrawingBrush(new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(45, 90, 105, 118)),
            null,
            new EllipseGeometry(new Point(10, 10), 0.85, 0.85)))
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 20, 20),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 20, 20)
        };
        tile.Freeze();
        dc.DrawRectangle(tile, null, new Rect(0, 0, Width, Height));
    }

    private void DrawSelectionBox(DrawingContext dc)
    {
        var rect = new Rect(_selectionBoxStart, _selectionBoxCurrent);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(28, 45, 127, 249)),
            new Pen(new SolidColorBrush(Color.FromRgb(45, 127, 249)), 1.5) { DashStyle = DashStyles.Dash }, rect);
    }

    private void DrawComponent(DrawingContext dc, ComponentModel component, CircuitSnapshot snapshot)
    {
        var size = ComponentCatalog.GetSize(component);
        dc.PushTransform(new TranslateTransform(component.X, component.Y));
        dc.PushTransform(new RotateTransform(component.Rotation, size.Width / 2, size.Height / 2));
        var categoryColor = CategoryColor(ComponentCatalog.GetCategory(component.Type));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(225, categoryColor.R, categoryColor.G, categoryColor.B)),
            new Pen(new SolidColorBrush(Color.FromArgb(180, (byte)Math.Max(0, categoryColor.R - 18), (byte)Math.Max(0, categoryColor.G - 18), (byte)Math.Max(0, categoryColor.B - 18))), 1.2),
            new Rect(-6, -6, size.Width + 12, size.Height + 12), 16, 16);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(28, 22, 30, 38)), null, new Rect(4, 6, size.Width, size.Height), 12, 12);
        switch (component.Type)
        {
            case ComponentType.ArduinoHeader: DrawArduino(dc, component); break;
            case ComponentType.Breadboard: DrawBreadboard(dc, component); break;
            case ComponentType.Led: DrawLed(dc, component, new CircuitEngine(_document).IsLedLit(component, snapshot)); break;
            case ComponentType.Resistor: DrawResistor(dc, component); break;
            case ComponentType.AcSource: DrawAcSource(dc, component); break;
            case ComponentType.WallPlug: DrawWallPlug(dc, component); break;
            case ComponentType.PowerStrip3:
            case ComponentType.PowerStrip6: DrawPowerStrip(dc, component); break;
            case ComponentType.Speaker4Ohm2W:
            case ComponentType.MihawkDriver: DrawSpeaker(dc, component); break;
            case ComponentType.Adapter5V2AUsbA:
            case ComponentType.Adapter5V2AUsbC:
            case ComponentType.AdapterFast18WUsbA:
            case ComponentType.AdapterFast20WUsbC:
            case ComponentType.AdapterFast65WUsbC:
                if (ComponentCatalog.IsImageMode(component)) DrawPhysicalPowerAdapter(dc, component);
                else DrawPowerAdapter(dc, component);
                break;
            case ComponentType.Rj45Plug:
            case ComponentType.Rj45Jack:
                if (ComponentCatalog.IsImageMode(component)) DrawPhysicalRj45(dc, component);
                else DrawRj45(dc, component);
                break;
            case ComponentType.EthernetCat5:
            case ComponentType.EthernetCat5e:
            case ComponentType.EthernetCat6:
            case ComponentType.EthernetCat6a:
            case ComponentType.EthernetCat7:
            case ComponentType.EthernetCat8:
                if (ComponentCatalog.IsImageMode(component)) DrawPhysicalEthernetCable(dc, component);
                else DrawEthernetCable(dc, component);
                break;
            case ComponentType.UsbAToLightningCable:
            case ComponentType.UsbCToUsbCCable:
            case ComponentType.UsbAToUsbACable:
            case ComponentType.UsbBToUsbBCable:
            case ComponentType.UsbAToUsbBCable:
            case ComponentType.UsbAToUsbBSuperSpeedCable:
            case ComponentType.UsbAToUsbCCable:
            case ComponentType.UsbCToLightningCable:
                if (ComponentCatalog.IsImageMode(component)) DrawPhysicalReadyCable(dc, component);
                else DrawReadyCable(dc, component);
                break;
            case ComponentType.DeviceUsbA:
            case ComponentType.DeviceUsbB:
            case ComponentType.DeviceUsbC:
            case ComponentType.DeviceLightning:
                DrawRechargeableDevice(dc, component, snapshot);
                break;
            default:
                if (ConnectorCatalog.TryGet(component.Type, out var connector))
                {
                    if (ComponentCatalog.IsImageMode(component)) DrawPhysicalConnector(dc, component, connector);
                    else DrawConnector(dc, component, connector);
                }
                break;
        }
        dc.Pop();
        dc.Pop();
    }

    private void DrawComponentOverlay(DrawingContext dc, ComponentModel component, bool selected)
    {
        var size = ComponentCatalog.GetSize(component);
        dc.PushTransform(new TranslateTransform(component.X, component.Y));
        dc.PushTransform(new RotateTransform(component.Rotation, size.Width / 2, size.Height / 2));
        if (ComponentCatalog.SupportsViewMode(component)) DrawViewToggle(dc, component);
        DrawRotationHandle(dc, component);
        if (selected && !ComponentCatalog.IsImageMode(component)) DrawContactGroupOverlays(dc, component);
        if (selected)
            dc.DrawRoundedRectangle(null, new Pen(Brushes.DodgerBlue, 2.5), new Rect(-4, -4, size.Width + 8, size.Height + 8), 12, 12);
        if (!ComponentCatalog.IsImageMode(component)) DrawPorts(dc, component);
        dc.Pop();
        dc.Pop();
    }

    private void DrawContactGroupOverlays(DrawingContext dc, ComponentModel component)
    {
        foreach (var group in ComponentCatalog.GetContactGroups(component))
        {
            var bounds = ComponentCatalog.GetContactGroupBoundsLocal(component, group.Id);
            if (bounds.IsEmpty) continue;
            var isSelected = _selectedGroupComponent == component && _selectedGroupId == group.Id;
            dc.DrawRoundedRectangle(isSelected ? new SolidColorBrush(Color.FromArgb(24, 45, 127, 249)) : null,
                new Pen(isSelected ? Brushes.DodgerBlue : new SolidColorBrush(Color.FromArgb(125, 54, 118, 177)), isSelected ? 2 : 1)
                { DashStyle = isSelected ? DashStyles.Solid : DashStyles.Dash }, bounds, 5, 5);
            if (isSelected) DrawText(dc, group.Label, 8, FontWeights.SemiBold, Brushes.DodgerBlue,
                new Point(bounds.X + 2, bounds.Y - 13));
            DrawContactGroupRotationHandle(dc, bounds);
        }
    }

    private static Rect ContactGroupRotationHandleRect(Rect bounds)
        => new(bounds.Right - 7, bounds.Top - 14, 18, 18);

    private static void DrawContactGroupRotationHandle(DrawingContext dc, Rect bounds)
    {
        var rect = ContactGroupRotationHandleRect(bounds);
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(64, 112, 168)), 1.3), center, 8, 8);
        dc.DrawText(MakeText("↻", 11, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(51, 90, 133))),
            new Point(center.X - 6, center.Y - 8));
    }

    private void DrawReadyCable(DrawingContext dc, ComponentModel component)
    {
        var size = ComponentCatalog.GetSize(component);
        dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(181, 191, 200)), 1.5), new Rect(0, 0, size.Width, size.Height), 14, 14);
        DrawTextCentered(dc, component.Name.ToUpperInvariant(), 13, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(57, 73, 86)), new Point(size.Width / 2, 19));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(52, 58, 64)), 9), new Point(83, 78), new Point(size.Width - 83, 78));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(45, 51, 57)), null, new Rect(33, 47, 65, 62), 8, 8);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(45, 51, 57)), null, new Rect(size.Width - 98, 47, 65, 62), 8, 8);
        var portsById = ComponentCatalog.GetPorts(component).ToDictionary(p => p.Id, StringComparer.Ordinal);
        foreach (var (left, right) in ReadyCableCatalog.Connections(component.Type))
        {
            if (!portsById.TryGetValue($"L_{left}", out var from) || !portsById.TryGetValue($"R_{right}", out var to)) continue;
            var route = CreateWireGeometry(from.Position, to.Position);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(85, 45, 54, 62)), 4.4), route);
            dc.DrawGeometry(null, new Pen(BrushFromColor(from.Color, Colors.Gray), 2.2), route);
        }
        foreach (var port in portsById.Values.Where(port => port.Id.StartsWith("L_", StringComparison.Ordinal) || port.Id.StartsWith("R_", StringComparison.Ordinal)))
        {
            var isLeft = port.Id.StartsWith("L_", StringComparison.Ordinal);
            var signal = port.Id[2..];
            var label = MakeText($"{(isLeft ? "L" : "R")} · {signal}", 8.5, FontWeights.SemiBold,
                new SolidColorBrush(Color.FromRgb(75, 84, 92)));
            var x = isLeft ? port.Position.X + 10 : port.Position.X - label.Width - 10;
            dc.DrawText(label, new Point(x, port.Position.Y - label.Height - 4));
        }
        if (component.Type == ComponentType.UsbAToUsbCCable)
            DrawTextCentered(dc, "56 kΩ CC bias", 9, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(124, 90, 28)), new Point(size.Width / 2, size.Height - 31));
        DrawTextCentered(dc, "factory wiring · editable after placement", 9, FontWeights.Normal, Brushes.DimGray, new Point(size.Width / 2, size.Height - 15));
    }

    private void DrawPhysicalReadyCable(DrawingContext dc, ComponentModel component)
    {
        var size = ComponentCatalog.GetSize(component);
        dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(181, 191, 200)), 1.5), new Rect(0, 0, size.Width, size.Height), 14, 14);
        if (component.Type == ComponentType.UsbAToUsbBSuperSpeedCable)
        {
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(39, 44, 49)), 10), new Point(72, 57), new Point(size.Width - 73, 57));
            var shell = new SolidColorBrush(Color.FromRgb(45, 50, 55));
            var metal = new SolidColorBrush(Color.FromRgb(190, 198, 203));
            var edge = new Pen(new SolidColorBrush(Color.FromRgb(113, 121, 127)), 1.2);
            dc.DrawRoundedRectangle(shell, null, new Rect(35, 31, 63, 52), 8, 8);
            dc.DrawRoundedRectangle(metal, edge, new Rect(78, 40, 47, 34), 3, 3);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(104, 111, 116)), null, new Rect(83, 45, 37, 6));
            dc.DrawRoundedRectangle(shell, null, new Rect(size.Width - 104, 29, 66, 56), 9, 9);
            var typeB = new StreamGeometry();
            using (var context = typeB.Open())
            {
                context.BeginFigure(new Point(size.Width - 141, 40), true, true);
                context.LineTo(new Point(size.Width - 93, 40), true, false);
                context.LineTo(new Point(size.Width - 89, 48), true, false);
                context.LineTo(new Point(size.Width - 91, 73), true, false);
                context.LineTo(new Point(size.Width - 96, 78), true, false);
                context.LineTo(new Point(size.Width - 137, 78), true, false);
                context.LineTo(new Point(size.Width - 142, 73), true, false);
                context.LineTo(new Point(size.Width - 144, 48), true, false);
            }
            typeB.Freeze();
            dc.DrawGeometry(metal, edge, typeB);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(107, 115, 121)), null,
                new Rect(size.Width - 135, 46, 34, 9), 2, 2);
            DrawTextCentered(dc, "USB 3.x STANDARD-A", 8, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(66, 77, 84)), new Point(72, 100));
            DrawTextCentered(dc, "USB 3.x STANDARD-B", 8, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(66, 77, 84)), new Point(size.Width - 73, 100));
            return;
        }
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(39, 44, 49)), 10), new Point(84, 56), new Point(size.Width - 84, 56));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(44, 49, 54)), null, new Rect(42, 30, 62, 52), 8, 8);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(44, 49, 54)), null, new Rect(size.Width - 104, 30, 62, 52), 8, 8);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(186, 194, 199)), new Pen(new SolidColorBrush(Color.FromRgb(118, 126, 132)), 1), new Rect(89, 38, 28, 35), 3, 3);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(186, 194, 199)), new Pen(new SolidColorBrush(Color.FromRgb(118, 126, 132)), 1), new Rect(size.Width - 117, 38, 28, 35), 3, 3);
        DrawTextCentered(dc, component.Name, 11, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(57, 73, 86)), new Point(size.Width / 2, 99));
    }

    private void DrawRechargeableDevice(DrawingContext dc, ComponentModel component, CircuitSnapshot snapshot)
    {
        var size = ComponentCatalog.GetSize(component);
        var charge = ReadCharge(component);
        var charging = IsDeviceCharging(component, snapshot);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(248, 250, 252)), new Pen(new SolidColorBrush(Color.FromRgb(169, 181, 191)), 1.5), new Rect(0, 0, size.Width, size.Height), 18, 18);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(42, 61, 76)), null, new Rect(18, 18, size.Width - 36, 72), 13, 13);
        DrawTextCentered(dc, component.Name.ToUpperInvariant(), 10, FontWeights.Bold, Brushes.White, new Point(size.Width / 2, 38));
        DrawTextCentered(dc, charging ? "CHARGING" : "ON BATTERY", 11, FontWeights.Bold, charging ? new SolidColorBrush(Color.FromRgb(117, 221, 144)) : new SolidColorBrush(Color.FromRgb(255, 196, 112)), new Point(size.Width / 2, 61));
        var battery = new Rect(36, 104, 102, 25);
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(71, 85, 96)), 2), battery, 4, 4);
        dc.DrawRectangle(new SolidColorBrush(charge > 20 ? Color.FromRgb(57, 180, 101) : Color.FromRgb(229, 87, 70)), null, new Rect(battery.X + 3, battery.Y + 3, (battery.Width - 6) * charge / 100, battery.Height - 6));
        DrawText(dc, $"{charge:0}%", 12, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(57, 70, 80)), new Point(146, 108));
        var work = new Rect(size.Width - 72, size.Height - 36, 58, 24);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(78, 106, 243)), null, work, 6, 6);
        DrawTextCentered(dc, "WORK", 9, FontWeights.Bold, Brushes.White, new Point(work.X + work.Width / 2, work.Y + 12));
    }

    private void DrawArduino(DrawingContext dc, ComponentModel component)
    {
        var board = new SolidColorBrush(Color.FromRgb(24, 123, 160));
        dc.DrawRoundedRectangle(board, new Pen(new SolidColorBrush(Color.FromRgb(13, 87, 116)), 2), new Rect(0, 0, 250, 300), 15, 15);
        DrawText(dc, "ARDUINO", 18, FontWeights.Bold, Brushes.White, new Point(18, 18));
        DrawText(dc, "CONNECTOR BOARD", 9, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(177, 227, 239)), new Point(19, 42));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(216, 221, 225)), new Pen(Brushes.White, 1), new Rect(-7, 74, 42, 58), 4, 4);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(75, 82, 88)), null, new Rect(-1, 86, 24, 34));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(30, 36, 41)), null, new Rect(58, 88, 102, 65), 5, 5);
        for (var i = 0; i < 12; i++)
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(205, 211, 215)), 2), new Point(64 + i * 8, 84), new Point(64 + i * 8, 78));
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(220, 223, 225)), null, new Point(71, 207), 22, 22);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(91, 99, 105)), null, new Point(71, 207), 8, 8);
        DrawText(dc, "USB", 10, FontWeights.Bold, Brushes.White, new Point(4, 138));
        DrawText(dc, "5 V power + digital I/O", 10, FontWeights.Normal, new SolidColorBrush(Color.FromRgb(201, 237, 244)), new Point(18, 274));
        foreach (var port in ComponentCatalog.GetPorts(component))
            DrawText(dc, port.Label, 10, FontWeights.SemiBold, Brushes.White, new Point(190, port.Position.Y - 7));
    }

    private void DrawBreadboard(DrawingContext dc, ComponentModel component)
    {
        var border = new Pen(new SolidColorBrush(Color.FromRgb(194, 201, 205)), 1.5);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(255, 254, 250)), border, new Rect(0, 0, 620, 310), 14, 14);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(246, 247, 244)), null, new Rect(20, 29, 580, 48), 8, 8);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(244, 245, 242)), new Pen(new SolidColorBrush(Color.FromRgb(224, 227, 224)), 1), new Rect(20, 80, 580, 158), 8, 8);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(233, 235, 232)), null, new Rect(24, 150, 572, 16), 3, 3);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(211, 214, 211)), 1), new Point(25, 158), new Point(595, 158));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(246, 247, 244)), null, new Rect(20, 254, 580, 48), 8, 8);

        var red = new SolidColorBrush(Color.FromRgb(230, 57, 53));
        var blue = new SolidColorBrush(Color.FromRgb(50, 133, 213));
        dc.DrawLine(new Pen(red, 2), new Point(50, 45), new Point(574, 45));
        dc.DrawLine(new Pen(blue, 2), new Point(50, 65), new Point(574, 65));
        dc.DrawLine(new Pen(red, 2), new Point(50, 270), new Point(574, 270));
        dc.DrawLine(new Pen(blue, 2), new Point(50, 291), new Point(574, 291));
        DrawText(dc, "BREADBOARD", 11, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(70, 78, 84)), new Point(12, 8));
        DrawText(dc, "+", 10, FontWeights.Bold, red, new Point(31, 37));
        DrawText(dc, "−", 10, FontWeights.Bold, blue, new Point(31, 57));
        DrawText(dc, "+", 10, FontWeights.Bold, red, new Point(31, 262));
        DrawText(dc, "−", 10, FontWeights.Bold, blue, new Point(31, 283));
        DrawText(dc, "a–e", 8, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(130, 137, 142)), new Point(575, 101));
        DrawText(dc, "f–j", 8, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(130, 137, 142)), new Point(575, 192));
        for (var number = 1; number <= 12; number++)
        {
            var x = 68 + (number - 1) * 44;
            DrawTextCentered(dc, number.ToString(CultureInfo.InvariantCulture), 8, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(96, 104, 110)), new Point(x, 83));
            DrawTextCentered(dc, number.ToString(CultureInfo.InvariantCulture), 8, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(96, 104, 110)), new Point(x, 244));
        }
        var letters = new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j" };
        for (var i = 0; i < 5; i++)
        {
            DrawText(dc, letters[i], 8, FontWeights.SemiBold, Brushes.Gray, new Point(39, 89 + i * 13));
            DrawText(dc, letters[i + 5], 8, FontWeights.SemiBold, Brushes.Gray, new Point(39, 164 + i * 13));
        }
    }

    private void DrawLed(DrawingContext dc, ComponentModel component, bool lit)
    {
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(255, 255, 255)), new Pen(new SolidColorBrush(Color.FromRgb(213, 218, 222)), 1), new Rect(0, 0, 100, 140), 12, 12);
        if (lit)
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(65, 255, 52, 42)), null, new Point(50, 62), 44, 44);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, 255, 78, 62)), null, new Point(50, 62), 34, 34);
        }
        var fill = lit ? Color.FromRgb(255, 55, 45) : Color.FromRgb(154, 53, 50);
        dc.DrawGeometry(new SolidColorBrush(fill), new Pen(new SolidColorBrush(Color.FromRgb(112, 38, 37)), 1.5), CreateLedBulb());
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(128, 134, 138)), 3), new Point(38, 90), new Point(28, 126));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(128, 134, 138)), 3), new Point(62, 90), new Point(72, 126));
        DrawTextCentered(dc, lit ? "LED · ON" : "LED · OFF", 10, FontWeights.Bold, lit ? Brushes.Red : Brushes.Gray, new Point(50, 15));
        DrawTextCentered(dc, "A        K", 9, FontWeights.SemiBold, Brushes.DimGray, new Point(50, 136));
    }

    private static Geometry CreateLedBulb()
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(new Point(28, 73), true, true);
        ctx.BezierTo(new Point(20, 38), new Point(32, 27), new Point(50, 27), true, true);
        ctx.BezierTo(new Point(68, 27), new Point(80, 38), new Point(72, 73), true, true);
        ctx.LineTo(new Point(68, 91), true, false);
        ctx.LineTo(new Point(32, 91), true, false);
        geometry.Freeze();
        return geometry;
    }

    private void DrawResistor(DrawingContext dc, ComponentModel component)
    {
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(255, 255, 255)), new Pen(new SolidColorBrush(Color.FromRgb(213, 218, 222)), 1), new Rect(0, 0, 180, 76), 11, 11);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(126, 132, 136)), 3), new Point(10, 38), new Point(45, 38));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(126, 132, 136)), 3), new Point(135, 38), new Point(170, 38));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(222, 195, 143)), new Pen(new SolidColorBrush(Color.FromRgb(159, 131, 86)), 1), new Rect(43, 22, 94, 32), 11, 11);
        var bands = new[] { Color.FromRgb(206, 38, 38), Color.FromRgb(206, 38, 38), Color.FromRgb(115, 64, 41), Color.FromRgb(212, 171, 43) };
        for (var i = 0; i < bands.Length; i++)
            dc.DrawRectangle(new SolidColorBrush(bands[i]), null, new Rect(58 + i * 18, 23, 6, 30));
        var resistance = component.Properties.TryGetValue("Resistance", out var value) ? value : "220";
        DrawTextCentered(dc, $"{resistance} Ω", 10, FontWeights.SemiBold, Brushes.DimGray, new Point(90, 11));
    }

    private void DrawAcSource(DrawingContext dc, ComponentModel component)
    {
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(255, 255, 255)), new Pen(new SolidColorBrush(Color.FromRgb(208, 213, 220)), 1.5), new Rect(0, 0, 190, 150), 12, 12);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(105, 67, 153)), null, new Rect(0, 0, 190, 38), 12, 12);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(105, 67, 153)), null, new Rect(0, 20, 190, 18));
        DrawText(dc, "SIMULATED AC SOURCE", 11, FontWeights.Bold, Brushes.White, new Point(13, 11));
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(248, 244, 252)), new Pen(new SolidColorBrush(Color.FromRgb(105, 67, 153)), 2), new Point(60, 91), 34, 34);
        var wave = new StreamGeometry();
        using (var ctx = wave.Open())
        {
            ctx.BeginFigure(new Point(31, 91), false, false);
            ctx.BezierTo(new Point(39, 70), new Point(48, 70), new Point(60, 91), true, false);
            ctx.BezierTo(new Point(72, 112), new Point(81, 112), new Point(89, 91), true, false);
        }
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(105, 67, 153)), 2.5), wave);
        var rms = component.Properties.TryGetValue("RmsVoltage", out var volts) ? volts : "5";
        var hz = component.Properties.TryGetValue("Frequency", out var frequency) ? frequency : "50";
        DrawText(dc, $"{rms} V RMS", 13, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(70, 52, 93)), new Point(104, 65));
        DrawText(dc, $"{hz} Hz", 11, FontWeights.Normal, Brushes.DimGray, new Point(104, 88));
        DrawText(dc, "L", 10, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(105, 67, 153)), new Point(145, 51));
        DrawText(dc, "N", 10, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(50, 133, 213)), new Point(145, 98));
        DrawText(dc, "simulation only", 9, FontWeights.Normal, Brushes.Gray, new Point(104, 112));
    }

    private void DrawConnector(DrawingContext dc, ComponentModel component, ConnectorDefinition connector)
    {
        var size = ComponentCatalog.GetSize(component.Type);
        var usb = connector.Category is ComponentCategory.Usb2Connectors or ComponentCategory.UsbSuperSpeedConnectors;
        var header = usb ? Color.FromRgb(45, 104, 160) : Color.FromRgb(72, 78, 86);
        dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(205, 211, 216)), 1.5), new Rect(0, 0, size.Width, size.Height), 12, 12);
        dc.DrawRoundedRectangle(new SolidColorBrush(header), null, new Rect(0, 0, size.Width, 54), 12, 12);
        dc.DrawRectangle(new SolidColorBrush(header), null, new Rect(0, 28, size.Width, 26));
        DrawText(dc, connector.Title, 14, FontWeights.Bold, Brushes.White, new Point(14, 8));
        DrawText(dc, connector.Subtitle, 9, FontWeights.Normal, new SolidColorBrush(Color.FromRgb(218, 230, 240)), new Point(14, 31));

        foreach (var port in ComponentCatalog.GetPorts(component))
        {
            var text = MakeText(port.Label, 9, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(67, 76, 84)));
            var onLeft = port.Position.X < size.Width / 2;
            var x = onLeft ? port.Position.X + 13 : port.Position.X - text.Width - 13;
            dc.DrawText(text, new Point(x, port.Position.Y - text.Height / 2));
            var lineStart = onLeft ? port.Position.X + 7 : port.Position.X - 7;
            var lineEnd = onLeft ? Math.Min(lineStart + 9, x - 2) : Math.Max(lineStart - 9, x + text.Width + 2);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(205, 211, 216)), 1), new Point(lineStart, port.Position.Y), new Point(lineEnd, port.Position.Y));
        }

        if (component.Type == ComponentType.UsbA2)
            DrawTextCentered(dc, "Socket contacts connect to matching solder pins", 8, FontWeights.Normal, Brushes.Gray, new Point(size.Width / 2, size.Height - 10));
        else
            DrawTextCentered(dc, "Every contact is independently wireable", 8, FontWeights.Normal, Brushes.Gray, new Point(size.Width / 2, size.Height - 10));
    }

    private void DrawPhysicalConnector(DrawingContext dc, ComponentModel component, ConnectorDefinition connector)
    {
        var size = ComponentCatalog.GetSize(component);
        dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(211, 217, 222)), 1.5), new Rect(0, 0, size.Width, size.Height), 14, 14);
        if (component.Type == ComponentType.UsbA2)
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(45, 137, 105)), new Pen(new SolidColorBrush(Color.FromRgb(29, 103, 77)), 1.5), new Rect(33, 28, 151, 67), 7, 7);
            for (var i = 0; i < 5; i++)
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(211, 172, 58)), new Pen(new SolidColorBrush(Color.FromRgb(128, 99, 34)), 1), new Point(50 + i * 28, 89), 5, 5);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(191, 198, 203)), new Pen(new SolidColorBrush(Color.FromRgb(116, 125, 132)), 1.5), new Rect(61, 19, 96, 57), 5, 5);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(227, 231, 233)), null, new Rect(68, 25, 82, 10));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(50, 55, 59)), null, new Rect(75, 64, 68, 17), 3, 3);
            DrawTextCentered(dc, "USB-A FEMALE BREAKOUT", 10, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(68, 77, 84)), new Point(size.Width / 2, size.Height - 12));
            return;
        }
        var centerY = 57d;
        var cable = new Pen(new SolidColorBrush(Color.FromRgb(45, 49, 53)), 9)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        dc.DrawLine(cable, new Point(6, centerY), new Point(70, centerY));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(47, 51, 54)),
            new Pen(new SolidColorBrush(Color.FromRgb(28, 31, 34)), 1.5), new Rect(58, 31, 74, 52), 8, 8);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(67, 72, 76)), null, new Rect(64, 37, 12, 40), 4, 4);

        var metal = new SolidColorBrush(Color.FromRgb(194, 201, 206));
        var edge = new Pen(new SolidColorBrush(Color.FromRgb(125, 133, 140)), 1.5);
        var tipX = 124d;
        if (component.Type is ComponentType.LightningPlug or ComponentType.LightningReceptacle)
        {
            dc.DrawRoundedRectangle(metal, edge, new Rect(tipX, 43, 55, 28), 4, 4);
            for (var i = 0; i < 6; i++)
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(202, 159, 53)), null, new Rect(tipX + 12 + i * 6, 47, 3, 20), 1, 1);
        }
        else if (component.Type is ComponentType.UsbCPlug or ComponentType.UsbCReceptacle)
        {
            dc.DrawRoundedRectangle(metal, edge, new Rect(tipX, 41, 65, 32), 12, 12);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(228, 232, 235)), 2), new Point(tipX + 12, 45), new Point(tipX + 52, 45));
        }
        else if (component.Type is ComponentType.UsbB2 or ComponentType.UsbB3)
        {
            var tip = new StreamGeometry();
            using (var ctx = tip.Open())
            {
                ctx.BeginFigure(new Point(tipX, 38), true, true);
                ctx.LineTo(new Point(tipX + 13, 31), true, false);
                ctx.LineTo(new Point(tipX + 57, 31), true, false);
                ctx.LineTo(new Point(tipX + 68, 39), true, false);
                ctx.LineTo(new Point(tipX + 68, 76), true, false);
                ctx.LineTo(new Point(tipX, 76), true, false);
            }
            tip.Freeze();
            dc.DrawGeometry(metal, edge, tip);
        }
        else if (component.Type is ComponentType.UsbMiniA or ComponentType.UsbMiniB or ComponentType.UsbMiniAb or
                 ComponentType.UsbMicroA or ComponentType.UsbMicroB or ComponentType.UsbMicroAb or
                 ComponentType.UsbMicroA3 or ComponentType.UsbMicroB3 or ComponentType.UsbMicroAb3)
        {
            var height = component.Type.ToString().Contains("Micro", StringComparison.Ordinal) ? 23d : 30d;
            dc.DrawRoundedRectangle(metal, edge, new Rect(tipX, centerY - height / 2, 64, height), 4, 4);
        }
        else
        {
            dc.DrawRoundedRectangle(metal, edge, new Rect(tipX, 36, 70, 42), 4, 4);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(150, 157, 163)), null, new Rect(tipX + 17, 37, 4, 8));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(150, 157, 163)), null, new Rect(tipX + 49, 37, 4, 8));
        }
        DrawTextCentered(dc, connector.Title, 10, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(68, 77, 84)), new Point(size.Width / 2, size.Height - 12));
    }

    private void DrawPowerAdapter(DrawingContext dc, ComponentModel component)
    {
        var size = ComponentCatalog.GetSize(component);
        var isUsbA = component.Type is ComponentType.Adapter5V2AUsbA or ComponentType.AdapterFast18WUsbA;
        var power = component.Properties.TryGetValue("MaxPower", out var watts) ? watts : "10";
        var profiles = component.Properties.TryGetValue("Profiles", out var profileText) ? profileText : "5V/2A";
        dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(201, 207, 212)), 1.5), new Rect(0, 0, size.Width, size.Height), 16, 16);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(42, 47, 51)), null, new Rect(48, 33, 125, 152), 20, 20);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(61, 67, 72)), null, new Rect(63, 51, 95, 83), 12, 12);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(175, 181, 185)), 8), new Point(79, 33), new Point(79, 10));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(175, 181, 185)), 8), new Point(141, 33), new Point(141, 10));
        if (isUsbA)
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(19, 23, 26)), new Pen(new SolidColorBrush(Color.FromRgb(145, 153, 159)), 2), new Rect(79, 146, 62, 25), 3, 3);
        else
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(19, 23, 26)), new Pen(new SolidColorBrush(Color.FromRgb(145, 153, 159)), 2), new Rect(85, 147, 50, 22), 11, 11);
        DrawTextCentered(dc, $"{power} W", 18, FontWeights.Bold, Brushes.White, new Point(110, 86));
        DrawTextCentered(dc, isUsbA ? "USB-A" : "USB-C", 10, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(205, 214, 220)), new Point(110, 111));
        DrawText(dc, component.Name.ToUpperInvariant(), 11, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(61, 70, 77)), new Point(13, size.Height - 27));
        DrawText(dc, profiles, 9, FontWeights.Normal, Brushes.Gray, new Point(184, 23));
        DrawText(dc, "AC IN", 9, FontWeights.Bold, Brushes.Gray, new Point(18, 55));
        var ports = ComponentCatalog.GetPorts(component).ToList();
        foreach (var port in ports.Where(p => p.Id.StartsWith("AC_", StringComparison.Ordinal)))
        {
            var label = port.Label.StartsWith("AC ", StringComparison.Ordinal) ? port.Label[3..] : port.Label;
            var text = MakeText(label, 8, FontWeights.SemiBold, BrushFromColor(port.Color, Colors.Gray));
            dc.DrawText(text, new Point(port.Position.X + 9, port.Position.Y - text.Height / 2));
        }
        foreach (var port in ports.Where(p => p.Id.StartsWith("USB_", StringComparison.Ordinal)))
        {
            var label = port.Label.Replace("USB-A ", "", StringComparison.Ordinal).Replace("USB-C ", "", StringComparison.Ordinal);
            var text = MakeText(label, 7.5, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(78, 87, 94)));
            var x = port.Position.X > size.Width * .75 ? port.Position.X - text.Width - 10 : port.Position.X + 10;
            dc.DrawText(text, new Point(x, port.Position.Y - text.Height / 2));
        }
    }

    private void DrawPhysicalPowerAdapter(DrawingContext dc, ComponentModel component)
    {
        var size = ComponentCatalog.GetSize(component);
        var isUsbA = component.Type is ComponentType.Adapter5V2AUsbA or ComponentType.AdapterFast18WUsbA;
        var power = component.Properties.TryGetValue("MaxPower", out var watts) ? watts : "10";
        dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(207, 213, 218)), 1.5), new Rect(0, 0, size.Width, size.Height), 14, 14);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(181, 186, 190)), 9) { StartLineCap = PenLineCap.Round }, new Point(33, 65), new Point(8, 65));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(181, 186, 190)), 9) { StartLineCap = PenLineCap.Round }, new Point(33, 101), new Point(8, 101));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(45, 49, 53)), new Pen(new SolidColorBrush(Color.FromRgb(27, 30, 33)), 1.5), new Rect(30, 27, 151, 112), 20, 20);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(61, 67, 72)), null, new Rect(45, 41, 121, 84), 13, 13);
        if (isUsbA)
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(18, 22, 25)), new Pen(new SolidColorBrush(Color.FromRgb(151, 159, 165)), 2), new Rect(153, 67, 42, 31), 4, 4);
        else
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(18, 22, 25)), new Pen(new SolidColorBrush(Color.FromRgb(151, 159, 165)), 2), new Rect(157, 71, 38, 23), 11, 11);
        DrawTextCentered(dc, $"{power} W", 18, FontWeights.Bold, Brushes.White, new Point(101, 72));
        DrawTextCentered(dc, isUsbA ? "USB-A" : "USB-C", 9, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(203, 211, 217)), new Point(101, 99));
        DrawTextCentered(dc, component.Name, 9.5, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(69, 78, 85)), new Point(size.Width / 2, size.Height - 13));
    }

    private void DrawRj45(DrawingContext dc, ComponentModel component)
    {
        var size = ComponentCatalog.GetSize(component);
        dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(202, 209, 214)), 1.5), new Rect(0, 0, size.Width, size.Height), 13, 13);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(55, 73, 85)), null, new Rect(0, 0, size.Width, 48), 13, 13);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(55, 73, 85)), null, new Rect(0, 27, size.Width, 21));
        DrawText(dc, component.Type == ComponentType.Rj45Plug ? "RJ45 PLUG" : "RJ45 FEMALE JACK", 14, FontWeights.Bold, Brushes.White, new Point(14, 14));
        var colors = ComponentCatalog.GetPorts(component).Where(p => p.Id != "SH").ToList();
        foreach (var port in colors)
        {
            dc.DrawRoundedRectangle(BrushFromColor(port.Color, Colors.Gray), null, new Rect(34, port.Position.Y - 6, 116, 12), 3, 3);
            DrawText(dc, port.Label, 9, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(63, 71, 77)), new Point(160, port.Position.Y - 7));
        }
        DrawText(dc, "T568B color order · all pins separate", 8.5, FontWeights.Normal, Brushes.Gray, new Point(22, 270));
    }

    private void DrawPhysicalRj45(DrawingContext dc, ComponentModel component)
    {
        var size = ComponentCatalog.GetSize(component);
        dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(210, 216, 220)), 1.5), new Rect(0, 0, size.Width, size.Height), 14, 14);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(47, 108, 178)), 10) { StartLineCap = PenLineCap.Round }, new Point(7, 61), new Point(75, 61));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(116, 183, 220)), new Pen(new SolidColorBrush(Color.FromRgb(65, 117, 148)), 1.5), new Rect(64, 34, 75, 54), 7, 7);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(170, 225, 240, 248)), new Pen(new SolidColorBrush(Color.FromRgb(122, 145, 157)), 1.2), new Rect(127, 29, 63, 64), 5, 5);
        for (var i = 0; i < 8; i++)
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(211, 166, 51)), null, new Rect(137 + i * 6, 37, 3, 35));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 188, 211, 222)), null, new Rect(141, 75, 39, 22));
        DrawTextCentered(dc, component.Name, 10, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(66, 75, 82)), new Point(size.Width / 2, size.Height - 12));
    }

    private void DrawEthernetCable(DrawingContext dc, ComponentModel component)
    {
        var size = ComponentCatalog.GetSize(component);
        dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(205, 212, 217)), 1.5), new Rect(0, 0, size.Width, size.Height), 13, 13);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(46, 111, 177)), null, new Rect(0, 0, size.Width, 42), 13, 13);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(46, 111, 177)), null, new Rect(0, 22, size.Width, 20));
        DrawText(dc, component.Name.ToUpperInvariant(), 14, FontWeights.Bold, Brushes.White, new Point(16, 11));
        DrawText(dc, "PIN", 8, FontWeights.Bold, Brushes.Gray, new Point(281, 46));
        for (var pin = 1; pin <= 8; pin++)
        {
            var left = ComponentCatalog.GetPorts(component).First(p => p.Id == $"L{pin}");
            var right = ComponentCatalog.GetPorts(component).First(p => p.Id == $"R{pin}");
            var brush = BrushFromColor(left.Color, Colors.Gray);
            dc.DrawLine(new Pen(brush, 7), new Point(left.Position.X + 8, left.Position.Y), new Point(right.Position.X - 8, right.Position.Y));
            var center = new Point((left.Position.X + right.Position.X) / 2, (left.Position.Y + right.Position.Y) / 2);
            DrawTextCentered(dc, pin.ToString(CultureInfo.InvariantCulture), 8, FontWeights.Bold, Brushes.White, center);
            DrawContactPinName(dc, left, true);
            DrawContactPinName(dc, right, false);
        }
        DrawTextCentered(dc, "Straight-through pin mapping · each conductor is independently wireable", 9, FontWeights.Normal, Brushes.Gray, new Point(size.Width / 2, 237));
    }

    private void DrawPhysicalEthernetCable(DrawingContext dc, ComponentModel component)
    {
        var size = ComponentCatalog.GetSize(component);
        dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(210, 216, 220)), 1.5), new Rect(0, 0, size.Width, size.Height), 14, 14);
        var cable = new Pen(new SolidColorBrush(Color.FromRgb(43, 112, 181)), 12) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(cable, new Point(72, 58), new Point(size.Width - 72, 58));
        DrawEthernetPlug(dc, 20, 28, false);
        DrawEthernetPlug(dc, size.Width - 20, 28, true);
        DrawTextCentered(dc, component.Name, 10, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(66, 75, 82)), new Point(size.Width / 2, size.Height - 14));
    }

    private static void DrawEthernetPlug(DrawingContext dc, double x, double y, bool flip)
    {
        dc.PushTransform(new ScaleTransform(flip ? -1 : 1, 1, x, y + 30));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(105, 175, 214)), new Pen(new SolidColorBrush(Color.FromRgb(65, 117, 148)), 1.5), new Rect(x, y + 6, 54, 48), 5, 5);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(175, 225, 240, 248)), new Pen(new SolidColorBrush(Color.FromRgb(122, 145, 157)), 1), new Rect(x + 43, y, 45, 60), 4, 4);
        for (var i = 0; i < 8; i++) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(211, 166, 51)), null, new Rect(x + 50 + i * 4, y + 7, 2, 28));
        dc.Pop();
    }

    private void DrawViewToggle(DrawingContext dc, ComponentModel component)
    {
        var size = ComponentCatalog.GetSize(component);
        var rect = new Rect(size.Width - 59, 8, 23, 23);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), new Pen(new SolidColorBrush(Color.FromRgb(107, 118, 128)), 1), rect, 5, 5);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(63, 75, 86)), 1.5);
        if (ComponentCatalog.IsImageMode(component))
        {
            for (var i = 0; i < 3; i++) dc.DrawLine(pen, new Point(rect.X + 5, rect.Y + 6 + i * 5), new Point(rect.Right - 5, rect.Y + 6 + i * 5));
        }
        else
        {
            dc.DrawRectangle(null, pen, new Rect(rect.X + 5, rect.Y + 5, 13, 12));
            dc.DrawLine(pen, new Point(rect.X + 6, rect.Bottom - 7), new Point(rect.X + 10, rect.Y + 11));
            dc.DrawLine(pen, new Point(rect.X + 10, rect.Y + 11), new Point(rect.Right - 5, rect.Bottom - 6));
        }
    }

    private void DrawRotationHandle(DrawingContext dc, ComponentModel component)
    {
        var size = ComponentCatalog.GetSize(component);
        var rect = new Rect(size.Width - 31, 8, 23, 23);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
            new Pen(new SolidColorBrush(Color.FromRgb(94, 106, 116)), 1), new Point(rect.X + 11.5, rect.Y + 11.5), 11.5, 11.5);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(63, 75, 86)), 1.7)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        var arc = new StreamGeometry();
        using (var ctx = arc.Open())
        {
            ctx.BeginFigure(new Point(rect.X + 7, rect.Y + 15), false, false);
            ctx.BezierTo(new Point(rect.X + 3, rect.Y + 7), new Point(rect.X + 15, rect.Y + 3), new Point(rect.X + 17, rect.Y + 9), true, false);
        }
        dc.DrawGeometry(null, pen, arc);
        dc.DrawLine(pen, new Point(rect.X + 17, rect.Y + 9), new Point(rect.X + 13.5, rect.Y + 8));
        dc.DrawLine(pen, new Point(rect.X + 17, rect.Y + 9), new Point(rect.X + 16, rect.Y + 5.5));
        var nearest = Math.Round(component.Rotation / 90) * 90;
        var snapped = Math.Abs(NormalizeSignedAngle(component.Rotation - nearest)) < 0.05;
        var angleText = snapped ? $"{NormalizeAngle(nearest):0}°" : $"{component.Rotation:0.#}°";
        DrawTextCentered(dc, angleText, 9, snapped ? FontWeights.Bold : FontWeights.Normal,
            snapped ? Brushes.Red : new SolidColorBrush(Color.FromRgb(82, 94, 104)), new Point(size.Width - 19, 40));
    }

    private void DrawWallPlug(DrawingContext dc, ComponentModel component)
    {
        dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(205, 211, 216)), 1.5), new Rect(0, 0, 220, 170), 14, 14);
        DrawText(dc, "TÜRKIYE · 230 V AC · 50 Hz", 10, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(78, 87, 95)), new Point(13, 11));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(238, 239, 237)), new Pen(new SolidColorBrush(Color.FromRgb(158, 164, 168)), 1.5), new Rect(26, 42, 105, 104), 22, 22);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(180, 183, 184)), null, new Rect(55, 24, 13, 52), 6, 6);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(180, 183, 184)), null, new Rect(89, 24, 13, 52), 6, 6);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(55, 59, 61)), null, new Point(78, 105), 9, 9);
        DrawText(dc, "L", 10, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(105, 67, 153)), new Point(166, 60));
        DrawText(dc, "N", 10, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(50, 133, 213)), new Point(166, 97));
        DrawText(dc, "PE", 10, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(39, 174, 96)), new Point(160, 134));
    }

    private void DrawPowerStrip(DrawingContext dc, ComponentModel component)
    {
        var size = ComponentCatalog.GetSize(component);
        var sockets = component.Type == ComponentType.PowerStrip3 ? 3 : 6;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(245, 245, 242)), new Pen(new SolidColorBrush(Color.FromRgb(176, 181, 183)), 2), new Rect(0, 0, size.Width, size.Height), 28, 28);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(70, 73, 75)), 12), new Point(0, 105), new Point(66, 105));
        DrawText(dc, $"{sockets}-SOCKET POWER STRIP", 11, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(74, 82, 88)), new Point(18, 12));
        var on = !component.Properties.TryGetValue("Power", out var state) || state.Equals("On", StringComparison.OrdinalIgnoreCase);
        var switchRect = new Rect(size.Width - 110, 13, 50, 23);
        dc.DrawRoundedRectangle(new SolidColorBrush(on ? Color.FromRgb(48, 176, 97) : Color.FromRgb(126, 136, 144)), null, switchRect, 10, 10);
        DrawTextCentered(dc, on ? "ON" : "OFF", 9, FontWeights.Bold, Brushes.White, new Point(switchRect.X + 25, switchRect.Y + 11));
        var ports = ComponentCatalog.GetPorts(component).ToList();
        foreach (var port in ports.Where(p => p.Id.StartsWith("IN_", StringComparison.Ordinal)))
            DrawContactPinName(dc, port, true);
        for (var socket = 1; socket <= sockets; socket++)
        {
            var center = new Point(175 + (socket - 1) * 100, 92);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(226, 228, 225)), new Pen(new SolidColorBrush(Color.FromRgb(142, 148, 150)), 1.5), center, 35, 35);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(48, 52, 54)), null, new Point(center.X - 11, center.Y - 4), 5, 8);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(48, 52, 54)), null, new Point(center.X + 11, center.Y - 4), 5, 8);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(39, 174, 96)), null, new Point(center.X, center.Y + 16), 4, 4);
            var socketPorts = ports.Where(p => p.Id.StartsWith($"S{socket}_", StringComparison.Ordinal)).ToList();
            if (socketPorts.Count > 0)
            {
                var groupCenter = new Point(socketPorts.Average(p => p.Position.X), socketPorts.Average(p => p.Position.Y));
                DrawTextCentered(dc, $"Socket {socket}", 8, FontWeights.Bold, Brushes.Gray, new Point(groupCenter.X, groupCenter.Y - 17));
                foreach (var port in socketPorts) DrawContactPinName(dc, port, true);
            }
        }
    }

    private static void DrawContactPinName(DrawingContext dc, PortDefinition port, bool toRight)
    {
        var label = port.Id switch
        {
            "IN_L" or "AC_L" => "L",
            "IN_N" or "AC_N" => "N",
            "IN_PE" or "AC_PE" => "PE",
            _ when port.Id.EndsWith("_L", StringComparison.Ordinal) => "L",
            _ when port.Id.EndsWith("_N", StringComparison.Ordinal) => "N",
            _ when port.Id.EndsWith("_PE", StringComparison.Ordinal) => "PE",
            _ => port.Label
        };
        var text = MakeText(label, 8, FontWeights.Bold, BrushFromColor(port.Color, Colors.Gray));
        var x = toRight ? port.Position.X + 8 : port.Position.X - text.Width - 8;
        dc.DrawText(text, new Point(x, port.Position.Y - text.Height / 2));
    }

    private void DrawSpeaker(DrawingContext dc, ComponentModel component)
    {
        var size = ComponentCatalog.GetSize(component);
        var mihawk = component.Type == ComponentType.MihawkDriver;
        dc.DrawRoundedRectangle(mihawk ? new SolidColorBrush(Color.FromRgb(28, 31, 36)) : Brushes.White,
            new Pen(new SolidColorBrush(Color.FromRgb(179, 185, 190)), 1.5), new Rect(0, 0, size.Width, size.Height), 14, 14);
        var center = new Point(size.Width / 2, 83);
        if (mihawk)
        {
            var headband = new StreamGeometry();
            using (var ctx = headband.Open())
            {
                ctx.BeginFigure(new Point(center.X - 48, center.Y - 20), false, false);
                ctx.BezierTo(new Point(center.X - 42, center.Y - 72), new Point(center.X + 42, center.Y - 72), new Point(center.X + 48, center.Y - 20), true, false);
            }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(100, 106, 112)), 8), headband);
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(114, 73, 181)), 7), center, 51, 51);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(22, 24, 28)), new Pen(new SolidColorBrush(Color.FromRgb(49, 166, 214)), 3), center, 39, 39);
            DrawTextCentered(dc, "50 mm", 12, FontWeights.Bold, Brushes.White, center);
            DrawTextCentered(dc, "MIHAWK · 32 Ω ±5", 10, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(220, 224, 228)), new Point(size.Width / 2, 18));
            DrawTextCentered(dc, "neodymium headphone driver", 8, FontWeights.Normal, new SolidColorBrush(Color.FromRgb(153, 163, 171)), new Point(size.Width / 2, 140));
        }
        else
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(65, 70, 75)), new Pen(new SolidColorBrush(Color.FromRgb(36, 40, 43)), 3), center, 53, 53);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(117, 124, 130)), null, center, 37, 37);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(43, 47, 50)), null, center, 18, 18);
            DrawTextCentered(dc, "4 Ω · 2 W SPEAKER", 11, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(70, 77, 83)), new Point(size.Width / 2, 16));
        }
        DrawTextCentered(dc, "+                 −", 10, FontWeights.Bold, mihawk ? Brushes.White : Brushes.DimGray, new Point(size.Width / 2, size.Height - 7));
    }

    private void DrawPorts(DrawingContext dc, ComponentModel component)
    {
        foreach (var port in ComponentCatalog.GetPorts(component))
        {
            var absoluteRef = new PortReference { ComponentId = component.Id, PortId = port.Id };
            var hovered = SamePort(absoluteRef, _hoverPort);
            var wired = _document.Wires.Any(w => SamePort(w.A, absoluteRef) || SamePort(w.B, absoluteRef));
            var fill = BrushFromColor(port.Color, Color.FromRgb(141, 153, 166));
            dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(86, 95, 103)), 1), port.Position, hovered ? 7.5 : 5.5, hovered ? 7.5 : 5.5);
            dc.DrawEllipse(fill, null, port.Position, wired ? 3.4 : 2.4, wired ? 3.4 : 2.4);
        }
    }

    private void DrawHoverTooltip(DrawingContext dc)
    {
        if (_hoverPort is null) return;
        var component = _document.Components.FirstOrDefault(c => c.Id == _hoverPort.ComponentId);
        if (component is null) return;
        var port = ComponentCatalog.GetPorts(component).FirstOrDefault(p => p.Id == _hoverPort.PortId);
        if (port is null) return;
        var position = ComponentCatalog.GetAbsolutePortPosition(_document, _hoverPort);
        var label = $"{component.Name}  ·  {port.Label}";
        var text = MakeText(label, 11, FontWeights.SemiBold, Brushes.White);
        text.MaxTextWidth = 390;
        text.Trimming = TextTrimming.CharacterEllipsis;
        var width = Math.Min(410, text.Width + 20);
        var x = Math.Clamp(position.X + 14, 5, Math.Max(5, Width - width - 5));
        var y = position.Y - 38;
        if (y < 5) y = position.Y + 16;
        var rect = new Rect(x, y, width, 28);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(248, 24, 30, 36)), new Pen(new SolidColorBrush(Color.FromRgb(86, 98, 109)), 1), rect, 6, 6);
        dc.DrawText(text, new Point(rect.X + 10, rect.Y + 6));
    }

    private void DrawAnnotation(DrawingContext dc, AnnotationModel annotation, bool selected)
    {
        var brush = BrushFromColor(annotation.Color, Color.FromRgb(42, 51, 59));
        if (annotation.Type == AnnotationType.Text)
        {
            var text = MakeText(annotation.Text, 17, FontWeights.SemiBold, brush);
            dc.PushTransform(new RotateTransform(annotation.Rotation, annotation.X1, annotation.Y1));
            dc.DrawText(text, new Point(annotation.X1, annotation.Y1));
            if (selected)
            {
                var bounds = AnnotationSelectionBounds(annotation, text);
                dc.DrawRoundedRectangle(null, new Pen(Brushes.DodgerBlue, 1.5), bounds, 4, 4);
                DrawAnnotationRotationHandle(dc, AnnotationRotationHandleRect(annotation, text));
            }
            dc.Pop();
            return;
        }

        var start = new Point(annotation.X1, annotation.Y1);
        var end = new Point(annotation.X2, annotation.Y2);
        if (selected) dc.DrawLine(new Pen(Brushes.DodgerBlue, 7), start, end);
        dc.DrawLine(new Pen(brush, 3), start, end);
        if (annotation.Type == AnnotationType.Arrow) DrawArrowHead(dc, brush, start, end);
        if (selected)
        {
            dc.DrawEllipse(Brushes.White, new Pen(Brushes.DodgerBlue, 2), start, 5, 5);
            dc.DrawEllipse(Brushes.White, new Pen(Brushes.DodgerBlue, 2), end, 5, 5);
        }
    }

    private void DrawAnnotationPreview(DrawingContext dc)
    {
        var brush = BrushFromColor(WireColor, Color.FromRgb(42, 51, 59));
        var pen = new Pen(brush, 2.5) { DashStyle = DashStyles.Dash };
        dc.DrawLine(pen, _annotationStart, _annotationCurrent);
        if (Mode == EditorMode.DrawArrow) DrawArrowHead(dc, brush, _annotationStart, _annotationCurrent);
    }

    private static void DrawArrowHead(DrawingContext dc, Brush brush, Point start, Point end)
    {
        var direction = end - start;
        if (direction.Length < 2) return;
        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        var basePoint = end - direction * 17;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(end, true, true);
            ctx.LineTo(basePoint + normal * 7, true, false);
            ctx.LineTo(basePoint - normal * 7, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private void DrawWire(DrawingContext dc, WireModel wire, bool selected)
    {
        if (IsImageEndpoint(wire.A) || (!wire.HasOpenEnd && IsImageEndpoint(wire.B))) return;
        var a = ComponentCatalog.GetAbsolutePortPosition(_document, wire.A);
        var b = wire.HasOpenEnd
            ? new Point(wire.OpenEndX, wire.OpenEndY)
            : ComponentCatalog.GetAbsolutePortPosition(_document, wire.B);
        var geometry = CreateWireGeometry(a, b);
        if (selected) dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(66, 133, 244)), 9), geometry);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(120, 15, 20, 24)), 6), geometry);
        dc.DrawGeometry(null, new Pen(BrushFromColor(wire.Color, Color.FromRgb(229, 57, 53)), 3.5), geometry);
        if (wire.HasOpenEnd)
        {
            var brush = BrushFromColor(wire.Color, Color.FromRgb(229, 57, 53));
            dc.DrawEllipse(Brushes.White, new Pen(brush, selected ? 3 : 2), b, selected ? 7 : 6, selected ? 7 : 6);
            dc.DrawEllipse(brush, null, b, 2.2, 2.2);
        }
    }

    private bool IsImageEndpoint(PortReference port)
        => _document.Components.FirstOrDefault(component => component.Id == port.ComponentId) is { } component &&
           ComponentCatalog.IsImageMode(component);

    private void DrawLooseWire(DrawingContext dc, Point a, Point b, string color)
    {
        var geometry = CreateWireGeometry(a, b);
        var pen = new Pen(BrushFromColor(color, Color.FromRgb(229, 57, 53)), 3) { DashStyle = DashStyles.Dash };
        dc.DrawGeometry(null, pen, geometry);
    }

    private static Geometry CreateWireGeometry(Point a, Point b)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        var bend = Math.Max(45, Math.Abs(b.X - a.X) * 0.42);
        var direction = b.X >= a.X ? 1 : -1;
        ctx.BeginFigure(a, false, false);
        ctx.BezierTo(new Point(a.X + bend * direction, a.Y), new Point(b.X - bend * direction, b.Y), b, true, false);
        geometry.Freeze();
        return geometry;
    }

    private void DrawProbeMarker(DrawingContext dc, PortReference? port, string label, Color color)
    {
        if (port is null) return;
        var point = ComponentCatalog.GetAbsolutePortPosition(_document, port);
        var brush = new SolidColorBrush(color);
        dc.DrawEllipse(Brushes.White, new Pen(brush, 3), point, 10, 10);
        var text = MakeText(label, 8, FontWeights.Bold, Brushes.White);
        var rect = new Rect(point.X - text.Width / 2 - 4, point.Y - 28, text.Width + 8, 16);
        dc.DrawRoundedRectangle(brush, null, rect, 4, 4);
        dc.DrawText(text, new Point(rect.X + 4, rect.Y + 2));
    }

    private PortReference? HitPort(Point point)
    {
        for (var i = _document.Wires.Count - 1; i >= 0; i--)
        {
            var wire = _document.Wires[i];
            if (wire.HasOpenEnd && (new Point(wire.OpenEndX, wire.OpenEndY) - point).Length <= 13)
                return new PortReference { ComponentId = wire.Id, PortId = "OPEN" };
        }
        for (var i = _document.Components.Count - 1; i >= 0; i--)
        {
            var component = _document.Components[i];
            if (ComponentCatalog.IsImageMode(component)) continue;
            foreach (var port in ComponentCatalog.GetPorts(component))
            {
                var absolute = ComponentCatalog.LocalToWorld(component, port.Position);
                if ((absolute - point).Length <= 11)
                    return new PortReference { ComponentId = component.Id, PortId = port.Id };
            }
        }
        return null;
    }

    private ComponentModel? HitComponent(Point point)
    {
        for (var i = _document.Components.Count - 1; i >= 0; i--)
        {
            var component = _document.Components[i];
            var size = ComponentCatalog.GetSize(component);
            var local = ComponentCatalog.WorldToLocal(component, point);
            if (new Rect(0, 0, size.Width, size.Height).Contains(local)) return component;
        }
        return null;
    }

    private ComponentModel? HitViewToggle(Point point)
    {
        for (var i = _document.Components.Count - 1; i >= 0; i--)
        {
            var component = _document.Components[i];
            if (!ComponentCatalog.SupportsViewMode(component)) continue;
            var size = ComponentCatalog.GetSize(component);
            var local = ComponentCatalog.WorldToLocal(component, point);
            var rect = new Rect(size.Width - 63, 4, 31, 31);
            if (rect.Contains(local)) return component;
        }
        return null;
    }

    private ComponentModel? HitRotationHandle(Point point)
    {
        for (var i = _document.Components.Count - 1; i >= 0; i--)
        {
            var component = _document.Components[i];
            var size = ComponentCatalog.GetSize(component);
            var local = ComponentCatalog.WorldToLocal(component, point);
            if (new Rect(size.Width - 35, 4, 31, 31).Contains(local)) return component;
        }
        return null;
    }

    private (ComponentModel Component, string GroupId)? HitContactGroupRotationHandle(Point point)
    {
        foreach (var component in _selectedComponents.Reverse())
        {
            if (ComponentCatalog.IsImageMode(component)) continue;
            var local = ComponentCatalog.WorldToLocal(component, point);
            foreach (var group in ComponentCatalog.GetContactGroups(component).Reverse())
            {
                var bounds = ComponentCatalog.GetContactGroupBoundsLocal(component, group.Id);
                if (!bounds.IsEmpty && ContactGroupRotationHandleRect(bounds).Contains(local)) return (component, group.Id);
            }
        }
        return null;
    }

    private (ComponentModel Component, string GroupId)? HitContactGroupBody(Point point)
    {
        foreach (var component in _selectedComponents.Reverse())
        {
            if (ComponentCatalog.IsImageMode(component)) continue;
            var local = ComponentCatalog.WorldToLocal(component, point);
            foreach (var group in ComponentCatalog.GetContactGroups(component).Reverse())
            {
                var bounds = ComponentCatalog.GetContactGroupBoundsLocal(component, group.Id);
                if (!bounds.IsEmpty && bounds.Contains(local)) return (component, group.Id);
            }
        }
        return null;
    }

    private AnnotationModel? HitAnnotationRotationHandle(Point point)
    {
        foreach (var annotation in _selectedAnnotations.Reverse())
        {
            if (annotation.Type != AnnotationType.Text) continue;
            var text = MakeText(annotation.Text, 17, FontWeights.SemiBold, Brushes.Black);
            var origin = new Point(annotation.X1, annotation.Y1);
            var handle = AnnotationRotationHandleRect(annotation, text);
            var localPoint = RotatePoint(point, origin, -annotation.Rotation);
            if (handle.Contains(localPoint)) return annotation;
        }
        return null;
    }

    private static Rect AnnotationSelectionBounds(AnnotationModel annotation, FormattedText text)
        => new(annotation.X1 - 5, annotation.Y1 - 4, text.Width + 28, Math.Max(text.Height + 8, 26));

    private static Rect AnnotationRotationHandleRect(AnnotationModel annotation, FormattedText text)
        => new(annotation.X1 + text.Width + 4, annotation.Y1 + Math.Max(0, (text.Height - 16) / 2), 16, 16);

    private static void DrawAnnotationRotationHandle(DrawingContext dc, Rect rect)
    {
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(64, 112, 168)), 1.2), center, 7, 7);
        var glyph = MakeText("↻", 9, FontWeights.Bold, new SolidColorBrush(Color.FromRgb(51, 90, 133)));
        dc.DrawText(glyph, new Point(center.X - glyph.Width / 2, center.Y - glyph.Height / 2 - 1));
    }

    private ComponentModel? HitPowerSwitch(Point point)
    {
        for (var i = _document.Components.Count - 1; i >= 0; i--)
        {
            var component = _document.Components[i];
            if (component.Type is not (ComponentType.PowerStrip3 or ComponentType.PowerStrip6)) continue;
            var size = ComponentCatalog.GetSize(component);
            if (new Rect(size.Width - 115, 8, 60, 33).Contains(ComponentCatalog.WorldToLocal(component, point))) return component;
        }
        return null;
    }

    private ComponentModel? HitWorkButton(Point point)
    {
        for (var i = _document.Components.Count - 1; i >= 0; i--)
        {
            var component = _document.Components[i];
            if (!ComponentCatalog.IsRechargeableDevice(component.Type)) continue;
            var size = ComponentCatalog.GetSize(component);
            var local = ComponentCatalog.WorldToLocal(component, point);
            if (new Rect(size.Width - 76, size.Height - 40, 66, 32).Contains(local)) return component;
        }
        return null;
    }

    private int AutoConnectCable(ComponentModel cable)
    {
        var size = ComponentCatalog.GetSize(cable);
        var sides = new[] { (Prefix: "L_", Position: ComponentCatalog.LocalToWorld(cable, new Point(8, size.Height / 2))), (Prefix: "R_", Position: ComponentCatalog.LocalToWorld(cable, new Point(size.Width - 8, size.Height / 2))) };
        var choices = (from side in sides
                       from target in _document.Components.Where(c => c != cable && !ComponentCatalog.IsReadyCable(c.Type))
                       where ReadyCableCatalog.CanMate(cable.Type, side.Prefix, target.Type) && IsNearComponent(target, side.Position)
                       select (Side: side, Target: target, Distance: (GetComponentCenter(target) - side.Position).Length))
            .OrderBy(choice => choice.Distance).ToList();
        var usedSides = new HashSet<string>(StringComparer.Ordinal);
        var usedTargets = new HashSet<string>(StringComparer.Ordinal);
        var added = 0;
        foreach (var choice in choices)
        {
            if (usedSides.Contains(choice.Side.Prefix) || usedTargets.Contains(choice.Target.Id)) continue;
            usedSides.Add(choice.Side.Prefix);
            usedTargets.Add(choice.Target.Id);
            foreach (var cablePort in ComponentCatalog.GetPorts(cable).Where(p => p.Id.StartsWith(choice.Side.Prefix, StringComparison.Ordinal)))
            {
                var signal = ComponentCatalog.GetPortSignal(cable, cablePort.Id);
                if (signal is null) continue;
                var candidates = ComponentCatalog.GetPorts(choice.Target).Where(p => ComponentCatalog.GetPortSignal(choice.Target, p.Id) == signal);
                if (choice.Target.Type == ComponentType.UsbA2) candidates = candidates.Where(p => p.Id.StartsWith("M_", StringComparison.Ordinal));
                var a = new PortReference { ComponentId = cable.Id, PortId = cablePort.Id };
                foreach (var targetPort in candidates)
                {
                    var b = new PortReference { ComponentId = choice.Target.Id, PortId = targetPort.Id };
                    if (WireExists(a, b)) continue;
                    _document.Wires.Add(new WireModel { A = a, B = b, Color = ComponentCatalog.SignalColor(signal) });
                    added++;
                }
            }
        }
        return added;
    }

    private static bool IsNearComponent(ComponentModel component, Point world)
    {
        var size = ComponentCatalog.GetSize(component);
        var local = ComponentCatalog.WorldToLocal(component, world);
        return new Rect(-65, -65, size.Width + 130, size.Height + 130).Contains(local);
    }

    private void UseDevice(ComponentModel device)
    {
        var snapshot = new CircuitEngine(_document).Analyze();
        if (IsDeviceCharging(device, snapshot))
        {
            device.Properties["WorkMode"] = "Working from external power";
            StatusChanged?.Invoke(this, $"{device.Name} is working from the charger; battery remains at {ReadCharge(device):0}%.");
        }
        else
        {
            var charge = Math.Max(0, ReadCharge(device) - 5);
            device.Properties["ChargePercent"] = charge.ToString("0", CultureInfo.InvariantCulture);
            device.Properties["WorkMode"] = "Worked on battery";
            StatusChanged?.Invoke(this, $"{device.Name} used battery power: {charge:0}% remaining.");
        }
        SelectComponent(device);
        NotifyDocumentChanged("Device work cycle completed");
    }

    private void AdvanceRechargeableDevices()
    {
        var devices = _document.Components.Where(c => ComponentCatalog.IsRechargeableDevice(c.Type)).ToList();
        if (devices.Count == 0) return;
        var snapshot = new CircuitEngine(_document).Analyze();
        var changed = false;
        foreach (var device in devices)
        {
            var working = device.Properties.TryGetValue("WorkMode", out var mode) && mode.StartsWith("Working", StringComparison.OrdinalIgnoreCase);
            if (IsDeviceCharging(device, snapshot))
            {
                if (working)
                {
                    device.Properties["WorkMode"] = "Idle";
                    changed = true;
                }
                else if (ReadCharge(device) < 100)
                {
                    device.Properties["ChargePercent"] = Math.Min(100, ReadCharge(device) + 1).ToString("0", CultureInfo.InvariantCulture);
                    device.Properties["WorkMode"] = "Charging";
                    changed = true;
                }
            }
            else if (working)
            {
                device.Properties["WorkMode"] = "Idle";
                changed = true;
            }
        }
        if (changed) NotifyDocumentChanged("Rechargeable device state updated");
    }

    private static double ReadCharge(ComponentModel device)
        => device.Properties.TryGetValue("ChargePercent", out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0, 100) : 0;

    private static bool IsDeviceCharging(ComponentModel device, CircuitSnapshot snapshot)
    {
        var vbus = snapshot.VoltageAt(new PortReference { ComponentId = device.Id, PortId = "VBUS" });
        var ground = snapshot.VoltageAt(new PortReference { ComponentId = device.Id, PortId = "GND" });
        return vbus is not null && ground is not null && vbus.Value - ground.Value >= 4.4;
    }

    private WireModel? HitWire(Point point)
    {
        foreach (var wire in _document.Wires.AsEnumerable().Reverse())
        {
            var a = ComponentCatalog.GetAbsolutePortPosition(_document, wire.A);
            var b = wire.HasOpenEnd
                ? new Point(wire.OpenEndX, wire.OpenEndY)
                : ComponentCatalog.GetAbsolutePortPosition(_document, wire.B);
            var previous = a;
            var bend = Math.Max(45, Math.Abs(b.X - a.X) * 0.42);
            var direction = b.X >= a.X ? 1 : -1;
            var c1 = new Point(a.X + bend * direction, a.Y);
            var c2 = new Point(b.X - bend * direction, b.Y);
            for (var step = 1; step <= 24; step++)
            {
                var t = step / 24.0;
                var current = Cubic(a, c1, c2, b, t);
                if (DistanceToSegment(point, previous, current) < 12) return wire;
                previous = current;
            }
        }
        return null;
    }

    private WireModel? HitOpenWireEnd(Point point)
        => _document.Wires.LastOrDefault(w => w.HasOpenEnd && (new Point(w.OpenEndX, w.OpenEndY) - point).Length <= 13);

    private void BeginOpenWireLift(WireModel wire, Point point)
    {
        _liftedWireIndex = _document.Wires.IndexOf(wire);
        _liftedOriginal = new WireModel { Id = wire.Id, A = wire.A.Copy(), B = wire.B.Copy(), Color = wire.Color, HasOpenEnd = true, OpenEndX = wire.OpenEndX, OpenEndY = wire.OpenEndY };
        _document.Wires.Remove(wire);
        wire.B = new PortReference();
        wire.HasOpenEnd = true;
        wire.OpenEndX = point.X;
        wire.OpenEndY = point.Y;
        _liftedWire = wire;
        _wireStart = wire.A.Copy();
        ClearSelection();
        StatusChanged?.Invoke(this, "Open wire end lifted — click a contact or empty space.");
        InvalidateVisual();
    }

    private AnnotationModel? HitAnnotation(Point point)
    {
        foreach (var annotation in _document.Annotations.AsEnumerable().Reverse())
        {
            if (annotation.Type == AnnotationType.Text)
            {
                var textPoint = RotatePoint(point, new Point(annotation.X1, annotation.Y1), -annotation.Rotation);
                var text = MakeText(annotation.Text, 17, FontWeights.SemiBold, Brushes.Black);
                if (new Rect(annotation.X1 - 6, annotation.Y1 - 6, Math.Max(34, text.Width) + 12, text.Height + 12).Contains(textPoint))
                    return annotation;
            }
            else if (DistanceToSegment(point, new Point(annotation.X1, annotation.Y1), new Point(annotation.X2, annotation.Y2)) < 9)
            {
                return annotation;
            }
        }
        return null;
    }

    private bool BeginWireLift(PortReference port, Point point)
    {
        var wire = _document.Wires.LastOrDefault(w => !w.HasOpenEnd && (SamePort(w.A, port) || SamePort(w.B, port)));
        if (wire is null) return false;

        _liftedWireIndex = _document.Wires.IndexOf(wire);
        _liftedOriginal = new WireModel
        {
            Id = wire.Id,
            A = wire.A.Copy(),
            B = wire.B.Copy(),
            Color = wire.Color,
            HasOpenEnd = wire.HasOpenEnd,
            OpenEndX = wire.OpenEndX,
            OpenEndY = wire.OpenEndY
        };
        var fixedEnd = SamePort(wire.A, port) ? wire.B.Copy() : wire.A.Copy();
        _document.Wires.Remove(wire);
        wire.A = fixedEnd;
        wire.B = new PortReference();
        wire.HasOpenEnd = true;
        wire.OpenEndX = point.X;
        wire.OpenEndY = point.Y;
        _liftedWire = wire;
        _wireStart = fixedEnd.Copy();
        ClearSelection();
        StatusChanged?.Invoke(this, "Wire end lifted — click another contact or empty space.");
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        return true;
    }

    private void CompleteLiftedWire(PortReference? destination, Point point)
    {
        if (_liftedWire is null) return;
        if (destination is not null && SamePort(_liftedWire.A, destination))
        {
            StatusChanged?.Invoke(this, "Choose a different contact or click empty space for an open end.");
            return;
        }

        if (destination is not null)
        {
            if (WireExists(_liftedWire.A, destination))
            {
                StatusChanged?.Invoke(this, "Those contacts are already connected. Choose another destination.");
                return;
            }
            _liftedWire.B = destination.Copy();
            _liftedWire.HasOpenEnd = false;
        }
        else
        {
            _liftedWire.B = new PortReference();
            _liftedWire.HasOpenEnd = true;
            _liftedWire.OpenEndX = Snap(point.X);
            _liftedWire.OpenEndY = Snap(point.Y);
        }

        _document.Wires.Add(_liftedWire);
        SelectWire(_liftedWire);
        _liftedWire = null;
        _liftedOriginal = null;
        _wireStart = null;
        NotifyDocumentChanged(destination is null ? "Wire moved to an open endpoint" : "Wire reconnected");
    }

    private void RestoreLiftedWire()
    {
        if (_liftedWire is null || _liftedOriginal is null) return;
        _liftedWire.A = _liftedOriginal.A.Copy();
        _liftedWire.B = _liftedOriginal.B.Copy();
        _liftedWire.Color = _liftedOriginal.Color;
        _liftedWire.HasOpenEnd = _liftedOriginal.HasOpenEnd;
        _liftedWire.OpenEndX = _liftedOriginal.OpenEndX;
        _liftedWire.OpenEndY = _liftedOriginal.OpenEndY;
        _document.Wires.Insert(Math.Clamp(_liftedWireIndex, 0, _document.Wires.Count), _liftedWire);
        _liftedWire = null;
        _liftedOriginal = null;
    }

    private static Point GetComponentCenter(ComponentModel component)
    {
        var size = ComponentCatalog.GetSize(component);
        return new Point(component.X + size.Width / 2, component.Y + size.Height / 2);
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= 360;
        return angle < 0 ? angle + 360 : angle;
    }

    private static double SnapAngle(double angle)
    {
        angle = NormalizeAngle(angle);
        var nearest = Math.Round(angle / 90) * 90;
        return Math.Abs(NormalizeSignedAngle(angle - nearest)) <= 5 ? NormalizeAngle(nearest) : Math.Round(angle, 1);
    }

    private static double AnnotationRotationAtPointer(Point pivot, Point pointer, double pointerOffset)
    {
        var pointerAngle = Math.Atan2(pointer.Y - pivot.Y, pointer.X - pivot.X) * 180 / Math.PI;
        return SnapAngle(pointerAngle + pointerOffset);
    }

    private static Point RotatePoint(Point point, Point center, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        return new Point(center.X + dx * Math.Cos(radians) - dy * Math.Sin(radians),
            center.Y + dx * Math.Sin(radians) + dy * Math.Cos(radians));
    }

    private static double NormalizeSignedAngle(double angle)
    {
        angle = NormalizeAngle(angle);
        return angle > 180 ? angle - 360 : angle;
    }

    private static Color CategoryColor(ComponentCategory category) => category switch
    {
        ComponentCategory.Controllers => Color.FromRgb(216, 239, 248),
        ComponentCategory.PowerSources => Color.FromRgb(235, 222, 247),
        ComponentCategory.Prototyping => Color.FromRgb(241, 235, 214),
        ComponentCategory.Indicators => Color.FromRgb(250, 222, 220),
        ComponentCategory.PassiveComponents => Color.FromRgb(240, 230, 211),
        ComponentCategory.Usb2Connectors => Color.FromRgb(218, 235, 250),
        ComponentCategory.UsbSuperSpeedConnectors => Color.FromRgb(211, 239, 245),
        ComponentCategory.LightningConnectors => Color.FromRgb(231, 233, 237),
        ComponentCategory.PowerDistribution => Color.FromRgb(230, 222, 244),
        ComponentCategory.Audio => Color.FromRgb(233, 222, 245),
        ComponentCategory.PowerAdapters => Color.FromRgb(250, 232, 204),
        ComponentCategory.Ethernet => Color.FromRgb(217, 240, 224),
        _ => Color.FromRgb(232, 236, 239)
    };

    private bool WireExists(PortReference a, PortReference b)
        => _document.Wires.Any(w => (SamePort(w.A, a) && SamePort(w.B, b)) || (SamePort(w.A, b) && SamePort(w.B, a)));

    private void NotifyDocumentChanged(string status)
    {
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        StatusChanged?.Invoke(this, status);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private static bool SamePort(PortReference? a, PortReference? b)
        => a is not null && b is not null && a.ComponentId == b.ComponentId && a.PortId == b.PortId;

    private static double Snap(double value) => Math.Round(value / 10) * 10;

    private static Point Cubic(Point p0, Point p1, Point p2, Point p3, double t)
    {
        var u = 1 - t;
        return new Point(u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
                         u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y);
    }

    private static double DistanceToSegment(Point point, Point a, Point b)
    {
        var ab = b - a;
        if (ab.LengthSquared < 0.001) return (point - a).Length;
        var t = Math.Clamp(Vector.Multiply(point - a, ab) / ab.LengthSquared, 0, 1);
        return (point - (a + ab * t)).Length;
    }

    private static FormattedText MakeText(string text, double size, FontWeight weight, Brush brush)
        => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, brush, 1.0);

    private static SolidColorBrush BrushFromColor(string? value, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(value) && ColorConverter.ConvertFromString(value) is Color color)
                return new SolidColorBrush(color);
        }
        catch
        {
            // Saved files can contain user-edited colors. Invalid values use a safe fallback.
        }
        return new SolidColorBrush(fallback);
    }

    private static void DrawText(DrawingContext dc, string text, double size, FontWeight weight, Brush brush, Point point)
        => dc.DrawText(MakeText(text, size, weight, brush), point);

    private static void DrawTextCentered(DrawingContext dc, string text, double size, FontWeight weight, Brush brush, Point center)
    {
        var formatted = MakeText(text, size, weight, brush);
        dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
    }
}
