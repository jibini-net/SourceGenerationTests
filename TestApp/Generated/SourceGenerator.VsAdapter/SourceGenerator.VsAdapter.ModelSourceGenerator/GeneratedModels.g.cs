﻿/* DO NOT EDIT THIS FILE */
#nullable disable
namespace Generated;
public class Permission
{
    public int perID { get; set; }
    public string perName { get; set; }
        = "";
    public string perConstant { get; set; }
        = "";
    public int perType { get; set; }
    public bool perActive { get; set; }
        = true;
    public int[] perTest { get; set; }
        = new[] { 1, 2, 3 };
}
public class SiteUser
{
    public int suID { get; set; }
    public string suEmail { get; set; }
        = "";
    public string suFirstName { get; set; }
        = "";
    public string suLastName { get; set; }
        = "";
    public string suPhone { get; set; }
        = "";
    public bool suActive { get; set; }
        = true;
    public bool suLocked { get; set; }
    public DateTime? suLastLogin { get; set; }
    public string suPassword { get; set; }
    public List<Permission> granted_permissions { get; set; }
        = new() /*{ <- escaped*/;
}
