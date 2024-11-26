using System;
using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSDNS.Tests;

[TestClass]
[TestSubject(typeof(DNSName))]
public class DNSNameTest
{
    [TestMethod]
    public void METHOD()
    {
        var dnsName = new DNSName("www.bupt.edu.cn"u8.ToArray());
        Console.WriteLine(dnsName.ToString());
        dnsName = new DNSName("www.bupt.edu.cn."u8.ToArray());
        Console.WriteLine(dnsName.ToString(trailing: false));
    }
}
