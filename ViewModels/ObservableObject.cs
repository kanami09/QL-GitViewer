using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickLook.Plugin.GitViewer.ViewModels
{
    /// <summary>
    ///     视图模型的公共基类，只提供变更通知。
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        ///     赋值并在值真的变了的时候发出通知，返回是否发生了变更。
        /// </summary>
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
