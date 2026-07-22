using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramPanel.Core.BatchTasks;
using TelegramPanel.Modules;
using TelegramPanel.Web.Modules;
using TelegramPanel.Web.Modules.BuiltIn;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class ModuleTaskCreationCatalogTests
{
    [Fact]
    public void Registry_keeps_all_definitions_but_only_exposes_valid_editors_for_creation()
    {
        var validEditorType = typeof(ValidTaskEditor).AssemblyQualifiedName!;
        var module = new TestTaskModule(
            new ModuleTaskDefinition
            {
                Category = "user",
                TaskType = "editor-backed",
                DisplayName = "有效编辑器",
                EditorComponentType = validEditorType
            },
            new ModuleTaskDefinition
            {
                Category = "user",
                TaskType = "route-only",
                DisplayName = "独立页面",
                CreateRoute = "/ext/test/settings"
            },
            new ModuleTaskDefinition
            {
                Category = "user",
                TaskType = "metadata-only",
                DisplayName = "仅任务元数据"
            },
            new ModuleTaskDefinition
            {
                Category = "user",
                TaskType = "not-a-component",
                DisplayName = "无效组件类型",
                EditorComponentType = typeof(string).AssemblyQualifiedName
            },
            new ModuleTaskDefinition
            {
                Category = "user",
                TaskType = "invalid-contract",
                DisplayName = "缺少编辑器参数",
                EditorComponentType = typeof(EditorWithoutDraftChanged).AssemblyQualifiedName
            },
            new ModuleTaskDefinition
            {
                Category = "user",
                TaskType = "route-and-editor",
                DisplayName = "独立页优先",
                CreateRoute = "/ext/test/settings",
                EditorComponentType = validEditorType
            });

        var contributions = CreateContributions(module, builtIn: false);

        Assert.Equal(6, contributions.Tasks.Count);
        Assert.Equal(6, contributions.TaskTypeToDefinition.Count);
        Assert.Equal("/ext/test/settings", contributions.TaskTypeToDefinition["route-only"].Definition.CreateRoute);

        var creatable = Assert.Single(contributions.CreatableTasks);
        Assert.Equal("editor-backed", creatable.Definition.TaskType);
        Assert.True(creatable.CanCreate);

        Assert.False(contributions.TaskTypeToDefinition["route-only"].CanCreate);
        Assert.False(contributions.TaskTypeToDefinition["metadata-only"].CanCreate);
        Assert.False(contributions.TaskTypeToDefinition["not-a-component"].CanCreate);
        Assert.False(contributions.TaskTypeToDefinition["invalid-contract"].CanCreate);
        Assert.False(contributions.TaskTypeToDefinition["route-and-editor"].CanCreate);
    }

    [Fact]
    public void Built_in_catalog_keeps_context_created_tasks_out_of_generic_create_dialog()
    {
        var contributions = CreateContributions(new TaskCatalogModule("1.0.0"), builtIn: true);

        Assert.Contains(BatchTaskTypes.ChannelInviteUsers, contributions.TaskTypeToDefinition.Keys);
        Assert.Contains(BatchTaskTypes.BotSetAdmins, contributions.TaskTypeToDefinition.Keys);

        var creatableTypes = contributions.CreatableTasks
            .Select(x => x.Definition.TaskType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(3, creatableTypes.Count);
        Assert.Contains(BatchTaskTypes.UserChatActive, creatableTypes);
        Assert.Contains(BatchTaskTypes.ChannelGroupPrivateCreate, creatableTypes);
        Assert.Contains(BatchTaskTypes.ChannelGroupPublicize, creatableTypes);
        Assert.DoesNotContain(BatchTaskTypes.ChannelInviteUsers, creatableTypes);
        Assert.DoesNotContain(BatchTaskTypes.BotSetAdmins, creatableTypes);
    }

    private static ModuleContributionRegistry CreateContributions(ITelegramPanelModule module, bool builtIn)
    {
        var context = new ModuleHostContext("1.0.0", Path.GetTempPath());
        var registry = new ModuleRegistry();
        registry.Add(new LoadedModule(
            module.Manifest.Id,
            module.Manifest.Version,
            builtIn,
            module,
            context,
            module.Manifest,
            ModuleRootPath: null));

        return new ModuleContributionRegistry(
            registry,
            NullLogger<ModuleContributionRegistry>.Instance);
    }

    public sealed class ValidTaskEditor : ComponentBase
    {
        [Parameter]
        public ModuleTaskDraft Draft { get; set; }

        [Parameter]
        public EventCallback<ModuleTaskDraft> DraftChanged { get; set; }
    }

    public sealed class EditorWithoutDraftChanged : ComponentBase
    {
        [Parameter]
        public ModuleTaskDraft Draft { get; set; }
    }

    private sealed class TestTaskModule : ITelegramPanelModule, IModuleTaskProvider
    {
        private readonly IReadOnlyList<ModuleTaskDefinition> _definitions;

        public TestTaskModule(params ModuleTaskDefinition[] definitions)
        {
            _definitions = definitions;
        }

        public ModuleManifest Manifest { get; } = new()
        {
            Id = "test.task-catalog",
            Name = "任务目录测试模块",
            Version = "1.0.0"
        };

        public void ConfigureServices(IServiceCollection services, ModuleHostContext context)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints, ModuleHostContext context)
        {
        }

        public IEnumerable<ModuleTaskDefinition> GetTasks(ModuleHostContext context) => _definitions;
    }
}
