using TelegramPanel.Modules;
using TelegramPanel.Web.Modules;

namespace TelegramPanel.Web.Services;

public static class ModuleTaskEditorComponentResolver
{
    public static Type? ResolveCreateEditor(RegisteredTaskDefinition taskDefinition)
    {
        return Resolve(taskDefinition, taskDefinition.Definition.EditorComponentType);
    }

    public static Type? ResolveEditEditor(RegisteredTaskDefinition taskDefinition)
    {
        var typeName = taskDefinition.Definition.TaskCenter.EditComponentType;
        if (string.IsNullOrWhiteSpace(typeName))
            typeName = taskDefinition.Definition.EditorComponentType;

        return Resolve(taskDefinition, typeName);
    }

    private static Type? Resolve(RegisteredTaskDefinition taskDefinition, string? typeName)
    {
        typeName = (typeName ?? string.Empty).Trim();
        if (typeName.Length == 0)
            return null;

        try
        {
            var moduleTypeName = NormalizeTypeName(typeName);
            var componentType = Type.GetType(typeName, throwOnError: false, ignoreCase: false)
                                ?? taskDefinition.Module.Instance.GetType().Assembly.GetType(moduleTypeName, throwOnError: false, ignoreCase: false);

            return HasEditorContract(componentType) ? componentType : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasEditorContract(Type? componentType)
    {
        if (componentType == null
            || componentType.IsAbstract
            || !typeof(Microsoft.AspNetCore.Components.IComponent).IsAssignableFrom(componentType))
            return false;

        return HasParameter<ModuleTaskDraft>(componentType, "Draft")
               && HasParameter<Microsoft.AspNetCore.Components.EventCallback<ModuleTaskDraft>>(componentType, "DraftChanged");
    }

    private static bool HasParameter<T>(Type componentType, string name)
    {
        var property = componentType.GetProperty(name);
        return property is { CanWrite: true }
               && property.PropertyType == typeof(T)
               && property.IsDefined(typeof(Microsoft.AspNetCore.Components.ParameterAttribute), inherit: true);
    }

    public static string NormalizeTypeName(string? typeName)
    {
        typeName = (typeName ?? string.Empty).Trim();
        if (typeName.Length == 0)
            return typeName;

        var comma = typeName.IndexOf(',', StringComparison.Ordinal);
        return comma > 0 ? typeName[..comma].Trim() : typeName;
    }
}
