using System;
using Godot;

namespace STS2Mobile.Launcher.Components;

// Observe gestures before child buttons consume GUI input. A normal tap still
// reaches the button; once dragging starts, cancel the button's pending click.
public partial class DragScrollContainer : ScrollContainer
{
    public Func<bool> GestureAllowed { get; set; }
    private int _pointer = -1; // -1: idle, -2: mouse, >= 0: touch index
    private Vector2 _origin;
    private double _initialScroll;
    private bool _dragging;
    private bool _followFocus;
    private ulong _ignoreMouseUntil;
    private readonly float _threshold;

    public DragScrollContainer(float scale)
    {
        _threshold = Math.Max(8, 10 * scale);
        ScrollDeadzone = (int)_threshold;
    }

    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree())
        {
            EndGesture();
            return;
        }
        if (GestureAllowed?.Invoke() == false)
        {
            EndGesture();
            // ScreenDrag can be delivered to the former GUI target even after
            // an overlay appears. Taps still reach the modal's own buttons.
            if (
                input is InputEventScreenDrag
                || input is InputEventMouseMotion { ButtonMask: MouseButtonMask.Left }
            )
                GetViewport().SetInputAsHandled();
            return;
        }
        switch (input)
        {
            case InputEventScreenTouch touch:
                if (touch.Pressed && _pointer == -1)
                    BeginGesture(touch.Position, touch.Index);
                else if (!touch.Pressed && _pointer == touch.Index)
                    EndGesture();
                _ignoreMouseUntil = Engine.GetProcessFrames() + 2;
                break;
            case InputEventScreenDrag drag when drag.Index == _pointer:
                MoveGesture(drag.Position);
                break;
            case InputEventMouseButton mouse when mouse.ButtonIndex == MouseButton.Left:
                if (_pointer >= 0 || Engine.GetProcessFrames() <= _ignoreMouseUntil)
                    return;
                if (mouse.Pressed)
                    BeginGesture(mouse.Position, -2);
                else if (_pointer == -2)
                    EndGesture();
                break;
            case InputEventMouseMotion mouse when _pointer == -2:
                if ((mouse.ButtonMask & MouseButtonMask.Left) != 0)
                    MoveGesture(mouse.Position);
                else
                    EndGesture();
                break;
        }
    }

    private void BeginGesture(Vector2 position, int pointer)
    {
        if (!GetGlobalRect().HasPoint(position) || GestureAllowed?.Invoke() == false)
            return;
        var bar = GetVScrollBar();
        if (bar.MaxValue <= bar.Page || (bar.Visible && bar.GetGlobalRect().HasPoint(position)))
            return;
        _pointer = pointer;
        _origin = position;
        _initialScroll = ScrollVertical;
        _dragging = false;
    }

    private void MoveGesture(Vector2 position)
    {
        if (!_dragging)
        {
            var delta = position - _origin;
            if (Math.Abs(delta.Y) < _threshold || Math.Abs(delta.Y) < Math.Abs(delta.X))
                return;
            _dragging = true;
            _followFocus = FollowFocus;
            FollowFocus = false;
            PropagateNotification((int)Control.NotificationScrollBegin);
            EmitSignal(ScrollContainer.SignalName.ScrollStarted);
        }
        ScrollVertical = (int)Math.Round(_initialScroll + _origin.Y - position.Y);
        GetViewport().SetInputAsHandled();
    }

    private void EndGesture()
    {
        if (_dragging)
        {
            PropagateNotification((int)Control.NotificationScrollEnd);
            FollowFocus = _followFocus;
            EmitSignal(ScrollContainer.SignalName.ScrollEnded);
        }
        _dragging = false;
        _pointer = -1;
    }
}
