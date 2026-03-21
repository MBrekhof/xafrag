using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafRag.Module.BusinessObjects;

[DefaultClassOptions]
[NavigationItem("Knowledge Base")]
[DefaultProperty(nameof(FileName))]
public class Document : IXafEntityObject
{
    [Key]
    public virtual int Id { get; set; }

    [FieldSize(500)]
    public virtual string FileName { get; set; } = string.Empty;

    public virtual FileData? FileData { get; set; }

    public virtual DocumentStatus Status { get; set; }

    public virtual DateTime CreatedDate { get; set; }

    public void OnCreated()
    {
        CreatedDate = DateTime.UtcNow;
        Status = DocumentStatus.Pending;
    }

    public void OnSaving()
    {
        if (FileData != null && !string.IsNullOrEmpty(FileData.FileName))
        {
            FileName = FileData.FileName;
        }
    }

    public void OnLoaded() { }
}
