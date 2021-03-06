﻿using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Acr.UserDialogs;
using Acr.SpeechRecognition;
using Plugin.TextToSpeech.Abstractions;


namespace Acr.SpeechDialogs.Impl
{
    public class SpeechDialogsImpl : ISpeechDialogs
    {
        readonly ISpeechRecognizer speech;
        readonly ITextToSpeech tts;
        readonly IUserDialogs dialogs;


        public SpeechDialogsImpl(ISpeechRecognizer sr, ITextToSpeech tts, IUserDialogs dialogs)
        {
            this.speech = sr;
            this.tts = tts;
            this.dialogs = dialogs;
        }


        public async void Actions(ActionsConfig config)
        {
            IDisposable dialog = null;
            IDisposable speech = null;
            var cancelSrc = new CancellationTokenSource();

            if (config.ShowDialog)
            {
                var dialogCfg = new ActionSheetConfig
                {
                    Title = config.Question
                };
                foreach (var choice in config.Choices)
                {
                    dialogCfg.Add(choice.Key, () =>
                    {
                        cancelSrc.Cancel();
                        speech.Dispose();
                        choice.Value?.Invoke();
                    });
                }
                dialog = this.dialogs.ActionSheet(dialogCfg);
            }

            // TODO: register for speech before TTS?  TTS may trigger this!



            await this.tts.Speak(config.Question, cancelToken: cancelSrc.Token);
            if (config.SpeakChoices)
            {
                foreach (var key in config.Choices.Keys)
                {
                    if (!cancelSrc.IsCancellationRequested)
                        await this.tts.Speak(key, cancelToken: cancelSrc.Token);
                }
            }
            var result = await this.speech.ListenForFirstKeyword(config.Choices.Keys.ToArray()).RunAsync(cancelSrc.Token);

            dialog?.Dispose();
            cancelSrc.Cancel();
            config.Choices[result]?.Invoke();
        }


        public async Task<bool> Confirm(string question, string positive, string negative, bool showDialog, CancellationToken? cancelToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            var cancelSrc = new CancellationTokenSource();

            IDisposable dialog = null;
            IDisposable speech = null;

            if (showDialog)
            {
                this.dialogs.Confirm(new ConfirmConfig
                {
                    Message = question,
                    OkText = positive,
                    CancelText = negative,
                    OnAction = dr =>
                    {
                        tcs.TrySetResult(dr);
                        speech.Dispose();
                        cancelSrc.Cancel();
                    }
                });
            }

            await this.tts.Speak(question, cancelToken: cancelSrc.Token);
            speech = this.speech
                .ListenForFirstKeyword(positive, negative)
                .Subscribe(text =>
                {
                    var r = text.Equals(positive, StringComparison.CurrentCultureIgnoreCase);
                    dialog?.Dispose();
                    tcs.TrySetResult(r);
                });

            cancelToken?.Register(() =>
            {
                dialog?.Dispose();
                speech.Dispose();
                tcs.TrySetCanceled();
            });

            return await tcs.Task;
        }


        public async Task<string> Prompt(string question, CancellationToken? cancelToken)
        {
            await this.tts.Speak(question);
            var result = await this.speech.ListenUntilPause().ToTask(cancelToken ?? CancellationToken.None);
            return result;
        }
    }
}
