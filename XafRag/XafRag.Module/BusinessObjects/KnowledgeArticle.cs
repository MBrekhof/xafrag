using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace XafRag.Module.BusinessObjects;

[DefaultClassOptions]
[NavigationItem("Knowledge Base")]
[DefaultProperty(nameof(Title))]
public class KnowledgeArticle : IXafEntityObject
{
    [Key]
    public virtual int Id { get; set; }

    [FieldSize(200)]
    public virtual string Title { get; set; } = string.Empty;

    [FieldSize(FieldSizeAttribute.Unlimited)]
    public virtual string Content { get; set; } = string.Empty;

    [FieldSize(500)]
    public virtual string? Tags { get; set; }

    public virtual DateTime CreatedDate { get; set; }
    public virtual DateTime ModifiedDate { get; set; }

    public void OnCreated()
    {
        CreatedDate = DateTime.UtcNow;
        ModifiedDate = DateTime.UtcNow;
    }

    public void OnSaving()
    {
        ModifiedDate = DateTime.UtcNow;
    }

    public void OnLoaded() { }
}
