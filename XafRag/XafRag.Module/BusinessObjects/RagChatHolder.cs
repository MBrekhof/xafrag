using System.ComponentModel;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace XafRag.Module.BusinessObjects;

[DomainComponent]
[DefaultClassOptions]
[NavigationItem("Knowledge Base")]
[DisplayName("RAG Chat")]
public class RagChatHolder : NonPersistentBaseObject
{
}
