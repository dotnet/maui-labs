namespace MauiTest1;

[TestClass]
public sealed class Test1
{
    [TestMethod]
    public void LabelText_CanBeSetAndRead()
    {
        var label = new Label
        {
            Text = "Hello, .NET MAUI!",
        };

        Assert.AreEqual("Hello, .NET MAUI!", label.Text);
    }
}
