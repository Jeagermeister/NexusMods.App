using System.Xml;

namespace Apocrypha.Games.MountAndBlade2Bannerlord;

/// <summary>
/// Hardened XML loading for mod-supplied files (<c>SubModule.xml</c>): DTDs are prohibited and no
/// external resolver is used, closing the entity-expansion ("billion laughs") and external-entity
/// DoS holes a plain <see cref="XmlDocument.Load(Stream)"/> leaves open on untrusted input
/// (CODE_REVIEW.md §7 #20 — same class as the FOMOD traversal).
/// </summary>
internal static class SecureXml
{
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <summary>Loads an <see cref="XmlDocument"/> from untrusted XML with DTDs prohibited.</summary>
    public static XmlDocument LoadUntrusted(Stream stream)
    {
        using var reader = XmlReader.Create(stream, Settings);
        var doc = new XmlDocument { XmlResolver = null };
        doc.Load(reader);
        return doc;
    }
}
