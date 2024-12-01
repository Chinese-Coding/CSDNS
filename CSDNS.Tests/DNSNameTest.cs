using System;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSDNS.Tests;

[TestClass]
[TestSubject(typeof(DNSName))]
public class DNSNameTest
{
    [TestMethod]
    public void NewMethodTest()
    {
        // TODO: 我想这个测试函数是不是没有什么意义? 因为下面很多函数在测试时都使用 new method
        var dnsName = new DNSName("www.bupt.edu.cn"u8.ToArray());
        Assert.AreEqual(dnsName.ToString(), "www.bupt.edu.cn.");
        var dnsName2 = new DNSName("www.bupt.edu.cn.");
        Assert.AreEqual(dnsName2.ToString(trailing: false), "www.bupt.edu.cn");
        // 合法长字符串测试
        var jpmens =
            "ccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc." +
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb." +
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa." +
            "test.xxx.yyy-yyyy.zzzzzzzzz-test.";
        var jpmensName = new DNSName(jpmens);
        Assert.AreEqual(jpmensName.ToString(), jpmens);
        Assert.ThrowsException<FormatException>(() => new DNSName("bert..Hubert."));

        // 过长字符串测试(依次是 label 大于 63, DNSName 总长度大于 255)
        Assert.ThrowsException<FormatException>(() =>
            new DNSName("1234567890123456789012345678901234567890123456789012345678901234567890.com."));
        Assert.ThrowsException<FormatException>(() =>
            new DNSName(
                "12345678901234567890.12345678901234567890123456.789012345678901.234567890.12345678901234567890." +
                "12345678901234567890123456.789012345678901.234567890.12345678901234567890.12345678901234567890123456." +
                "789012345678901.234567890.234567890.789012345678901.234567890.234567890.789012345678901.234567890.234567890.com."));
    }

    [TestMethod]
    public void OperatorTest()
    {
        var wwwds9anl = new DNSName("www.ds9a.nl.");
        Assert.IsTrue(wwwds9anl == wwwds9anl); // 指向相同对象
        Assert.IsTrue(new DNSName("wWw.ds9A.Nl.") == new DNSName("www.ds9a.nl.")); // 大小写不同
        Assert.IsTrue(new DNSName("www.ds9a.nl.") == new DNSName("www.ds9a.nl.")); // 创建两个对象然后比较 (创建方式相同, 但指向不同)

        DNSName name = new("."), parent = new();
        Assert.IsTrue(name != parent);

        // TODO: 这里有两个比较函数没有实现 (因为看不懂具体含义), `<` 以及 `canonCompare`
    }

    [TestMethod]
    public void IsIncludeTest()
    {
        DNSName wwwds9anl = new("www.ds9a.nl."), wwwds9anl1 = new("www.ds9a\002nl."), nl = new("nl.");
        Assert.IsTrue(wwwds9anl.IsInclude(nl));
        Assert.IsTrue(!wwwds9anl1.IsInclude(nl));
        Assert.IsTrue(wwwds9anl.IsInclude(wwwds9anl));
        Assert.IsTrue(!nl.IsInclude(wwwds9anl));

        // Check name part of root
        {
            DNSName name = new("a."), parent = new(".");
            Assert.IsTrue(name.IsInclude(parent));
        }
        // Label boundary
        {
            DNSName name = new("a\002bb."), parent = new("bb.");
            Assert.IsTrue(!name.IsInclude(parent));
        }
        // Multi label parent
        {
            DNSName name = new("a.bb.ccc.dddd."), parent = new("ccc.dddd.");
            Assert.IsTrue(name.IsInclude(parent));
        }
        // Last char diff (测试等号之类的, 还真测试出来一个)
        {
            DNSName name = new("a.bb.ccc.dddd."), parent = new("ccc.dddx.");
            Assert.IsTrue(!name.IsInclude(parent));
        }
        // Equal length identical
        {
            DNSName name = new("aaaa.bbb.cc.d."), parent = new("aaaa.bbb.cc.d.");
            Assert.IsTrue(name.IsInclude(parent));
        }
        // Equal length first char diff
        {
            DNSName name = new("xaaa.bbb.cc.d."), parent = new("aaaa.bbb.cc.d.");
            Assert.IsTrue(!name.IsInclude(parent));
        }
    }

    /// <summary>
    /// 一些比较简单的函数的测试
    /// </summary>
    [TestMethod]
    public void LittleFunctionTest()
    {
        // GetRawLabelsTest
        {
            var before = "www.ds9a.nl.";
            var b = new DNSName(before);
            Assert.AreEqual(b.GetRawLabels().Count, 3);
            var after = b.ToString();
            Assert.AreEqual(before, after);
        }
        // LabelReverseTest
        {
            var name = new DNSName("aaaa.bbb.cc.d.");
            Assert.IsTrue(name.LabelReverse() == new DNSName("d.cc.bbb.aaaa.")); // 注意这里使用 `==` 进行比较而不是 `AreEquals`
        }
        // MakeRelativeTest
        {
            DNSName name = new("aaaa.bbb.cc.d."), parent = new("cc.d.");
            Assert.IsTrue(name.MakeRelative(parent) == new DNSName("aaaa.bbb."));
        }
        // EmptyTest
        {
            var name = new DNSName();
            Assert.IsTrue(name.Empty());
        }
        // RootTest
        {
            DNSName name = new("."), rootnodot = new("");
            // 这个测试代码有问题, 在参考项目中 emoty 是直接被声明成 `string empty`, 可能和 C++ 和 C# 语言特性有关
            // String empty; Assert.IsTrue(new DNSName(empty) == name);
            Assert.IsTrue(!name.Empty());
            Assert.IsTrue(name == rootnodot);
        }
    }

    [TestMethod]
    public void PrependAndAppendRawLabelTest()
    {
        // TODO: 对 `Prepend` 的测试有点少
        var left = new DNSName("ds9a.nl.");
        left.PrependRawLabel("www");
        Assert.IsTrue(left == new DNSName("WwW.dS9A.Nl."));
        left.AppendRawLabel("com");
        Assert.IsTrue(left == new DNSName("WwW.Ds9A.Nl.com."));

        var unset = new DNSName();
        unset.AppendRawLabel("www").AppendRawLabel("powerdns.com").AppendRawLabel("com");
        Assert.IsTrue(unset.ToString() == "www.powerdns\\.com.com.");

        var build = new DNSName();
        build.AppendRawLabel("Donald E. Eastlake 3rd");
        build.AppendRawLabel("example");
        Assert.IsTrue(build.ToString() == @"Donald\032E\.\032Eastlake\0323rd.example.");
    }

    /// <summary>
    /// 最重要的测试, 设计如何处理各种转义字符的问题
    /// </summary>
    [TestMethod]
    public void RFC4343Test()
    {
        // 2024:11/30 17:16 通过写单元测试还真检查出一些错误
        DNSName rfc4343_2_1 = new("~!.example."),
            rfc4343_2_2 = new(@"Donald\032E\.\032Eastlake\0323rd.example."),
            example = new("example.");
        Assert.IsTrue(rfc4343_2_1.IsInclude(example));
        Assert.IsTrue(rfc4343_2_2.IsInclude(example));
        Assert.IsTrue(rfc4343_2_1.ToString() == "~!.example.");
        var labels = rfc4343_2_2.GetRawLabels();
        Assert.IsTrue(labels[0].SequenceEqual("Donald E. Eastlake 3rd"u8.ToArray()));
        Assert.IsTrue(labels[^1].SequenceEqual("example"u8.ToArray()));
        Assert.IsTrue(labels.Count == 2);
    }
}
