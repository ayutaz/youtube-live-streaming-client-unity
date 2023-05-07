#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mochineko.Relent.Extensions.NewtonsoftJson;
using Mochineko.Relent.Result;
using Mochineko.Relent.UncertainResult;

namespace Mochineko.YouTubeLiveStreamingClient
{
    public static class VideosAPI
    {
        private const string EndPoint = "/videos";
        
        public static async UniTask<IUncertainResult<LiveStreamingDetails>>
            GetLiveStreamingDetailsAsync(
                HttpClient httpClient,
                string apiKey,
                string videoID,
                CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(videoID))
            {
                return UncertainResults.FailWithTrace<LiveStreamingDetails>(
                    $"Failed because {nameof(videoID)} is null or empty.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return UncertainResults.RetryWithTrace<LiveStreamingDetails>(
                    "Retryable because cancellation has been already requested.");
            }

            // Build path parameters
            var parameters = new Dictionary<string, string>()
            {
                ["part"] = "liveStreamingDetails",
                ["id"] = videoID,
                ["key"] = apiKey,
            };
            var pathParameters = await new FormUrlEncodedContent(parameters)
                .ReadAsStringAsync();

            // Build request message
            var requestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                YouTubeAPIURL.BaseURL + EndPoint + "?" + pathParameters);

            // Send request
            HttpResponseMessage responseMessage;
            var apiResult = await UncertainTryFactory
                .TryAsync<HttpResponseMessage>(async innerCancellationToken
                    => await httpClient.SendAsync(requestMessage, innerCancellationToken))
                .CatchAsRetryable<HttpResponseMessage, HttpRequestException>(exception
                    => $"Retryable because -> {exception}.")
                .CatchAsRetryable<HttpResponseMessage, OperationCanceledException>(exception
                    => $"Retryable because -> {exception}.")
                .CatchAsFailure<HttpResponseMessage, Exception>(exception
                    => $"Failure because -> {exception}.")
                .ExecuteAsync(cancellationToken);
            switch (apiResult)
            {
                case IUncertainSuccessResult<HttpResponseMessage> apiSuccess:
                    responseMessage = apiSuccess.Result;
                    break;

                case IUncertainRetryableResult<HttpResponseMessage> apiRetryable:
                    return UncertainResults.RetryWithTrace<LiveStreamingDetails>(
                        $"Retryable because -> {apiRetryable.Message}.");

                case IUncertainFailureResult<HttpResponseMessage> apiFailure:
                    return UncertainResults.FailWithTrace<LiveStreamingDetails>(
                        $"Failed because -> {apiFailure.Message}.");

                default:
                    throw new UncertainResultPatternMatchException(nameof(apiResult));
            }

            // Succeeded
            if (responseMessage.IsSuccessStatusCode)
            {
                var responseJson = await responseMessage.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(responseJson))
                {
                    return UncertainResults.FailWithTrace<LiveStreamingDetails>(
                        $"Failed because response string was null.");
                }

                var deserializeResult = RelentJsonSerializer
                    .Deserialize<VideosAPIResponse>(responseJson);
                switch (deserializeResult)
                {
                    case ISuccessResult<VideosAPIResponse> deserializeSuccess:
                        var response = deserializeSuccess.Result;
                        if (response.Items.Count != 0)
                        {
                            return UncertainResults.Succeed(
                                response
                                    .Items[0]
                                    .LiveStreamingDetails);
                        }
                        else
                        {
                            return UncertainResults.FailWithTrace<LiveStreamingDetails>(
                                $"Failed because response items was empty.");
                        }

                    case IFailureResult<VideosAPIResponse> deserializeFailure:
                        return UncertainResults.FailWithTrace<LiveStreamingDetails>(
                            $"Failed to deserialize json to dictionary because -> {deserializeFailure.Message}.");

                    default:
                        throw new ResultPatternMatchException(nameof(deserializeResult));
                }
            }
            // Retryable
            else if (responseMessage.StatusCode is HttpStatusCode.TooManyRequests
                     || (int)responseMessage.StatusCode is >= 500 and <= 599)
            {
                return UncertainResults.RetryWithTrace<LiveStreamingDetails>(
                    $"Retryable because the API returned status code:({(int)responseMessage.StatusCode}){responseMessage.StatusCode}.");
            }
            // Response error
            else
            {
                return UncertainResults.FailWithTrace<LiveStreamingDetails>(
                    $"Failed because the API returned status code:({(int)responseMessage.StatusCode}){responseMessage.StatusCode}."
                );
            }
        }
    }
}