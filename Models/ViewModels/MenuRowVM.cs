using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace FlowDesk.Models.ViewModels
{
    public class MenuRowVM
    {
        public long Id { get; set; }
        public long ParentId { get; set; }
        public byte MenuType { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string Icon { get; set; }
        public int Sort { get; set; }
        public bool IsVisible { get; set; }
        public byte Status { get; set; }

        public int Level { get; set; }        // 层级（0/1）
        public bool HasChildren { get; set; } // 是否有子节点
    }
}