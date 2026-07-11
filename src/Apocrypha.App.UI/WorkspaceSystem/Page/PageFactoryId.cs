using TransparentValueObjects;

namespace Apocrypha.App.UI.WorkspaceSystem;

[ValueObject<Guid>]
public readonly partial struct PageFactoryId : IAugmentWith<JsonAugment>;
