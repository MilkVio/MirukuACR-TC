using Dalamud.Bindings.ImGui;
using ECommons.ExcelServices;
using ECommons.Logging;
using MilkVio.Healer.Common;
using MilkVio.Healer.AST;
using MilkVio.Healer.AST.Openers;
using MilkVio.Healer.AST.Resolvers.Gcd;
using MilkVio.Healer.AST.Resolvers.OffGcd;
using MilkVio.Healer.AST.Timeline;
using PromeRotation.Data;
using PromeRotation.Managers;
using PromeRotation.Resolvers;
using PromeRotation.Rotation;
using PromeRotation.Timeline;
using PromeRotation.Timeline.Core;

namespace MilkVio.Healer.AST;

[RotationMetadata((uint)Job.AST, "Mio Astrologian", "MilkVio", GlobalVersion.Version, ContentScope = AcrContentScope.HighEnd)]
public sealed class AstrologianRotation : IRotation
{
    private readonly IRotationEventHandler _eventHandler = new DefaultRotationEventHandler();
    private readonly List<IDecisionResolver> _alwaysResolvers = new();
    private readonly List<IDecisionResolver> _gcdResolvers = new();
    private readonly List<IDecisionResolver> _offGcdResolvers = new();
    private readonly AstrologianRotationContext _context = new();

    public static IReadOnlyDictionary<string, bool> QtList { get; } = new Dictionary<string, bool>
    {
        { HealerQt.启用起手, true },
        { HealerQt.停手, false },
        { HealerQt.爆发药, false },
        { HealerQt.AOE, true },
        { HealerQt.DOT, true },
        { HealerQt.醒梦, true },
        { HealerQt.占卜, true },
        { HealerQt.抽卡, true },
        { HealerQt.发卡, true },
        { HealerQt.剑卡, true },
        { HealerQt.冠卡, false },
        { HealerQt.光速, true },
        { HealerQt.倒计时地星, true },
    };

    private static readonly string[] OpenerNames =
    [
        占星标准起手.OpenerDisplayName,
    ];

    public static IReadOnlyDictionary<string, Type> Openers { get; } = new Dictionary<string, Type>
    {
        { OpenerNames[0], typeof(占星标准起手) },
    };

    public static IJobNodeProvider? NodeProvider { get; } = new AstrologianJobNodeProvider();

    public AstrologianRotation()
    {
        // oGCD 从上往下判定：同一帧只会取第一个 Check() 通过的技能。
        // 因此越靠前越会抢占插入窗口，120s 爆发轴相关动作需要放在醒梦等兜底动作前。
        _offGcdResolvers.Add(new 光速OffGcd());
        _offGcdResolvers.Add(new 占卜OffGcd());
        _offGcdResolvers.Add(new 发卡OffGcd(_context));
        _offGcdResolvers.Add(new 王冠卡OffGcd(_context));
        _offGcdResolvers.Add(new 抽卡OffGcd(_context));
        _offGcdResolvers.Add(new 神谕OffGcd());
        _offGcdResolvers.Add(new 醒梦OffGcd());

        // GCD 从上往下判定：AOE 优先，其次续 DOT，最后凶星作为单体填充兜底。
        // 治疗/复活 GCD 文件仍保留在目录中，但当前不注册，所以不会参与循环。
        _gcdResolvers.Add(new 重力Gcd());
        _gcdResolvers.Add(new 焚灼Gcd());
        _gcdResolvers.Add(new 凶星Gcd());

        foreach (var (name, def) in QtList)
            PromeSettings.Instance.AddQt(name, def);
    }

    public IRotationEventHandler GetEventHandler() => _eventHandler;

    public IOpener? GetOpener()
    {
        if (!PromeSettings.Instance.GetQt(HealerQt.启用起手) || Core.Me == null)
            return null;

        if (TryCreateTimelineOpener(out var timelineOpener))
            return timelineOpener;

        return CreateOpener(SelectedOpenerName(), "设置页");
    }

    private static bool TryCreateTimelineOpener(out IOpener? opener)
    {
        var openerName = PromeRotation.PureTimeline.PtlManager.CurrentOpener;
        var openerSource = "PureTimeline";

        if (string.IsNullOrWhiteSpace(openerName))
        {
            var meta = TimelineManager.CurrentMeta;
            openerName = meta?.Opener;
            openerSource = "Timeline";
        }

        if (string.IsNullOrWhiteSpace(openerName))
        {
            opener = null;
            return false;
        }

        opener = CreateOpener(openerName, openerSource);
        return opener != null;
    }

    private static IOpener? CreateOpener(string openerName, string openerSource)
    {
        if (!Openers.TryGetValue(openerName, out var openerType))
        {
            PluginLog.Warning($"[ACR] {openerSource} 指定起手不存在：{openerName}");
            return null;
        }

        try
        {
            if (Activator.CreateInstance(openerType) is IOpener opener)
            {
                PluginLog.Information($"[ACR] 从{openerSource}加载起手：{openerName}");
                return opener;
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"[ACR] 创建起手实例失败: {ex.Message}");
        }

        return null;
    }

    public PAction? NextAlways() => Resolve(_alwaysResolvers);

    public PAction? NextGcd() => Resolve(_gcdResolvers);

    public PAction? NextOffGcd() => Resolve(_offGcdResolvers);

    public void UpdateDebugStatus()
    {
        RotationManager.AlwaysSolverStatus.Clear();
        RotationManager.GcdSolverStatus.Clear();
        RotationManager.OffGcdSolverStatus.Clear();

        foreach (var resolver in _alwaysResolvers)
        {
            var result = resolver.Check();
            RotationManager.AlwaysSolverStatus.Add(new SolverStatus
            {
                Name = resolver.GetType().Name,
                Success = result.Success,
                Message = result.Message
            });
        }

        foreach (var resolver in _gcdResolvers)
        {
            var result = resolver.Check();
            RotationManager.GcdSolverStatus.Add(new SolverStatus
            {
                Name = resolver.GetType().Name,
                Success = result.Success,
                Message = result.Message
            });
        }

        foreach (var resolver in _offGcdResolvers)
        {
            var result = resolver.Check();
            RotationManager.OffGcdSolverStatus.Add(new SolverStatus
            {
                Name = resolver.GetType().Name,
                Success = result.Success,
                Message = result.Message
            });
        }
    }

    public void DrawQTs() => DrawGeneral();

    public void DrawSettings()
    {
        if (!ImGui.BeginTabBar($"Settings##{nameof(AstrologianRotation)}")) return;

        if (ImGui.BeginTabItem("设置"))
        {
            DrawAstSettings();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("开发用"))
        {
            DrawDev();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private static string SelectedOpenerName()
    {
        var selectedIndex = Math.Clamp(MioAcrSettings.Instance.AstOpenerIndex, 0, OpenerNames.Length - 1);
        return OpenerNames[selectedIndex];
    }

    private static void DrawAstSettings()
    {
        var openerIndex = Math.Clamp(MioAcrSettings.Instance.AstOpenerIndex, 0, OpenerNames.Length - 1);
        if (ImGui.BeginCombo("占星起手", SelectedOpenerName()))
        {
            for (var i = 0; i < OpenerNames.Length; i++)
            {
                var isSelected = i == openerIndex;
                if (ImGui.Selectable(OpenerNames[i], isSelected))
                {
                    MioAcrSettings.Instance.AstOpenerIndex = i;
                    MioAcrSettings.Save();
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        var slideMode = Math.Clamp(MioAcrSettings.Instance.AstSlideMode, 0, 2);
        var slideNames = new[] { "滑步续毒", "移动停手", "相位吉星" };
        if (ImGui.BeginCombo("移动策略", slideNames[slideMode]))
        {
            for (var i = 0; i < slideNames.Length; i++)
            {
                var isSelected = i == slideMode;
                if (ImGui.Selectable(slideNames[i], isSelected))
                {
                    MioAcrSettings.Instance.AstSlideMode = i;
                    MioAcrSettings.Save();
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

    }

    private void DrawGeneral()
    {
        foreach (var (name, _) in QtList)
        {
            var value = PromeSettings.Instance.GetQt(name);
            if (ImGui.Checkbox(name, ref value))
                PromeSettings.Instance.SetQt(name, value);
        }
    }

    private void DrawDev()
    {
        var me = HealerUtils.Me;
        var cards = HealerUtils.AstDrawnCards();
        ImGui.Text($"Job:{(uint)Job.AST}");
        ImGui.Text($"Level:{me?.Level ?? 0}");
        ImGui.Text($"MP:{me?.CurrentMp ?? 0}");
        ImGui.Text($"ActiveDraw:{HealerUtils.AstActiveDrawText()}");
        ImGui.Text($"Card1:{(cards.Length > 0 ? cards[0] : "None")}");
        ImGui.Text($"Crown:{HealerUtils.AstDrawnCrownCard()}");
    }

    private static PAction? Resolve(IEnumerable<IDecisionResolver> resolvers)
    {
        foreach (var resolver in resolvers)
        {
            if (resolver.Check().Success)
                return resolver.GetAction();
        }

        return null;
    }
}
