using EasyFramework.UI;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public sealed class ReactivePropertyTests
    {
        [Test]
        public void ValueChange_NotifiesDistinctValues_AndCanUnsubscribe()
        {
            var property = new ReactiveProperty<int>(1);
            int calls = 0;
            int latest = 0;
            var subscription = property.Subscribe(value => { calls++; latest = value; });

            property.Value = 1;
            property.Value = 2;
            subscription.Dispose();
            property.Value = 3;

            Assert.AreEqual(2, calls);
            Assert.AreEqual(2, latest);
        }

        [Test]
        public void BoxedSetter_ConvertsCompatibleNumericValue()
        {
            IReactiveProperty property = new ReactiveProperty<int>();

            Assert.IsTrue(property.TrySetValue(7f));
            Assert.AreEqual(7, property.BoxedValue);
        }
    }
}
