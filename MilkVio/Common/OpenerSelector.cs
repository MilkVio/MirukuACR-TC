using Dalamud.Bindings.ImGui;
using ECommons.Logging;
using PromeRotation.Rotation;

namespace MilkVio.Common;

// 通用 UI 兜底起手选择器。宿主已在更高层处理自定义/时间轴起手的覆盖，
// 只有都不存在时才回退到 Rotation.GetOpener()。选择仅存内存，不持久化。
public sealed class OpenerSelector
{
    private string? _selected;

    public void DrawCombo(string label, IReadOnlyDictionary<string, Type> openers)
    {
        if (openers.Count == 0)
        {
            ImGui.TextDisabled($"{label}：无可用起手");
            return;
        }

        var current = Current(openers);
        if (!ImGui.BeginCombo(label, current)) return;

        foreach (var name in openers.Keys)
        {
            var isSelected = name == current;
            if (ImGui.Selectable(name, isSelected))
                _selected = name;

            if (isSelected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    public IOpener? Resolve(IReadOnlyDictionary<string, Type> openers)
    {
        if (openers.Count == 0)
            return null;

        var name = Current(openers);
        if (!openers.TryGetValue(name, out var openerType))
            return null;

        try
        {
            if (Activator.CreateInstance(openerType) is IOpener opener)
            {
                PluginLog.Information($"[ACR] 从设置页加载起手：{name}");
                return opener;
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"[ACR] 创建起手实例失败: {ex.Message}");
        }

        return null;
    }

    private string Current(IReadOnlyDictionary<string, Type> openers)
    {
        if (_selected != null && openers.ContainsKey(_selected))
            return _selected;

        return openers.Keys.First();
    }
}
