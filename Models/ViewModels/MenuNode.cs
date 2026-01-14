using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace FlowDesk.Models.ViewModels
{
    public class MenuNode
    {
        public long Id { get; set; }
        public long ParentId { get; set; }
        public string Name { get; set; }
        public byte MenuType { get; set; }   // 1目录 2菜单
        public string Url { get; set; }
        public string Icon { get; set; }
        public int Sort { get; set; }

        public List<MenuNode> Children { get; set; } = new List<MenuNode>();
    }
}