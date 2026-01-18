using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace FlowDesk.Models.ViewModels
{
    using System;

    namespace FlowDesk.Models.ViewModels
    {
        public class TicketCommentVM
        {
            public long Id { get; set; }
            public long TicketId { get; set; }
            public long UserId { get; set; }
            public string UserName { get; set; }     // 展示名：RealName(UserName)
            public string Content { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}