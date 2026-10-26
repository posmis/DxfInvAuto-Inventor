using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace InvAuto.Framework.Ui
{
    /// <summary>Базовый класс для моделей представления с уведомлением об изменениях.</summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
