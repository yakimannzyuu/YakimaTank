using UnityEngine;

namespace Yakimannzyuu.YakimaTank.Editor
{
    /// <summary>
    /// Play 中の作業値。作った元の設定と組にして持ち、元の設定が替わったら捨てて作り直す。
    /// 替わった後も前の複製を使い続けると、前の設定の値で、別の設定を上書きしてしまう。
    /// </summary>
    internal sealed class YakimaTankWorkingValues
    {
        private YakimaTankSetupSettings _source;
        private YakimaTankSetupSettings _values;

        public YakimaTankSetupSettings For(YakimaTankSetupSettings source)
        {
            if (_values != null && _source == source)
            {
                return _values;
            }

            Discard();
            _values = Object.Instantiate(source);
            _values.hideFlags = HideFlags.DontSave;
            _source = source;
            return _values;
        }

        public void Discard()
        {
            if (_values != null)
            {
                Object.DestroyImmediate(_values);
            }

            _values = null;
            _source = null;
        }
    }
}
